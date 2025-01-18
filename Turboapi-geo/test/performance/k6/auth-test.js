import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Store test users
let testUsers = new SharedArray('test users', function() {
    return Array.from({ length: 10 }, (_, i) => ({
        email: `locationtester${i}@example.com`,
        password: 'TestPassword123!'
    }));
});

export const options = {
    scenarios: {
        smoke: {
            executor: 'constant-vus',
            vus: 1,
            duration: '30s',
            tags: { test_type: 'smoke' },
        },
        load: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                { duration: '2m', target: 50 },
                { duration: '5m', target: 50 },
                { duration: '2m', target: 0 },
            ],
            tags: { test_type: 'load' },
        },
        stress: {
            executor: 'ramping-arrival-rate',
            startRate: 1,
            timeUnit: '1s',
            preAllocatedVUs: 100,
            maxVUs: 100,
            stages: [
                { duration: '2m', target: 10 },
                { duration: '5m', target: 20 },
                { duration: '2m', target: 30 },
                { duration: '1m', target: 0 },
            ],
            tags: { test_type: 'stress' },
        },
    },
    thresholds: {
        'http_req_duration{name:CreateLocation}': ['p(95)<500'],
        'http_req_duration{name:UpdateLocation}': ['p(95)<400'],
        'http_req_duration{name:GetLocation}': ['p(95)<300'],
        'http_req_duration{name:GetLocationsInExtent}': ['p(95)<400'],
        'http_req_duration{name:DeleteLocation}': ['p(95)<400'],
        'http_req_failed': ['rate<0.01'],
    },
};

const AUTH_URL = 'http://localhost:5098';
const GEO_URL = 'http://localhost:5028';

function randomCoordinates() {
    return {
        longitude: Math.random() * 360 - 180,
        latitude: Math.random() * 180 - 90
    };
}

// Authenticate and get cookies for a user
function authenticateUser(user) {
    const loginPayload = JSON.stringify({
        email: user.email,
        password: user.password
    });

    const loginRes = http.post(`${AUTH_URL}/api/auth/login`, loginPayload, {
        headers: { 'Content-Type': 'application/json' }
    });

    if (loginRes.status === 200) {
        // Get cookies from response
        const token =  JSON.parse(loginRes.body).accessToken;
        return { token, createdLocations: [] };
    }

    // If login fails, try registering
    const registerPayload = JSON.stringify({
        email: user.email,
        password: user.password,
        confirmPassword: user.password
    });

    const registerRes = http.post(`${AUTH_URL}/api/auth/register`, registerPayload, {
        headers: { 'Content-Type': 'application/json' }
    });

    if (registerRes.status === 200) {
        // Login after registration
        const loginAfterRegister = http.post(`${AUTH_URL}/api/auth/login`, loginPayload, {
            headers: { 'Content-Type': 'application/json' }
        });

        if (loginAfterRegister.status === 200) {
            const token =  JSON.parse(loginAfterRegister.body).accessToken;
            return { token, createdLocations: [] };
        }
    }

    return null;
}

export function setup() {
    const sessions = testUsers.map(user => {
        const session = authenticateUser(user);
        if (session) {
            return {
                user: user,
                token: session.token,
                createdLocations: []
            };
        }
        return null;
    }).filter(session => session !== null);

    console.log(`Setup complete. ${sessions.length} users authenticated.`);
    return { sessions };
}

export default function(data) {
    if (!data.sessions || data.sessions.length === 0) {
        console.log('No authenticated sessions available');
        return;
    }

    const session = data.sessions[Math.floor(Math.random() * data.sessions.length)];
    const requestConfig = {
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${session.token}`
        }
    };
    const rand = Math.random();

    if (rand < 0.3) {
        performCreateLocation(requestConfig, session);
    } else if (rand < 0.5) {
        performUpdateLocation(requestConfig, session);
    } else if (rand < 0.7) {
        performGetLocation(requestConfig, session);
    } else if (rand < 0.9) {
        performGetLocationsInExtent(requestConfig);
    } else {
        performDeleteLocation(requestConfig, session);
    }

    // Occasionally test unauthorized access
    if (Math.random() < 0.1) {
        performUnauthorizedAccess();
    }

    sleep(1);
}

function performCreateLocation(config, session) {
    const coords = randomCoordinates();
    const payload = JSON.stringify({
        longitude: coords.longitude,
        latitude: coords.latitude
    });

    const res = http.post(`${GEO_URL}/api/locations`, payload, {
       ...config,
        tags: { name: 'CreateLocation' }
    });

    if (check(res, {
        'create location success': (r) => r.status === 201,
        'has location id': (r) => JSON.parse(r.body) !== undefined
    })) {
        session.createdLocations.push(res.json());
    }
}

function performUpdateLocation(config, session) {
    if (session.createdLocations.length === 0) {
        performCreateLocation(config, session);
        return;
    }

    const locationId = session.createdLocations[
        Math.floor(Math.random() * session.createdLocations.length)
        ];
    const coords = randomCoordinates();
    const payload = JSON.stringify({
        longitude: coords.longitude,
        latitude: coords.latitude
    });

    const res = http.put(
        `${GEO_URL}/api/locations/${locationId}/position`,
        payload,
        {
            ...config,
            tags: { name: 'UpdateLocation' }
        }
    );

    check(res, {
        'update location success': (r) => r.status === 204
    });
}

function performGetLocation(config, session) {
    if (session.createdLocations.length === 0) {
        performCreateLocation(config, session);
        return;
    }

    const locationId = session.createdLocations[
        Math.floor(Math.random() * session.createdLocations.length)
        ];

    const res = http.get(
        `${GEO_URL}/api/locations/${locationId}`,
        {
            ...config,
            tags: { name: 'GetLocation' }
        }
    );

    check(res, {
        'get location success': (r) => r.status === 200,
        'has valid location data': (r) => {
            const body = JSON.parse(r.body);
            return body.id && typeof body.longitude === 'number' && typeof body.latitude === 'number';
        }
    });
}

function performGetLocationsInExtent(config) {
    const minLon = Math.random() * 360 - 180;
    const maxLon = minLon + (Math.random() * 10);
    const minLat = Math.random() * 180 - 90;
    const maxLat = minLat + (Math.random() * 10);

    const res = http.get(
        `${GEO_URL}/api/locations?minLon=${minLon}&maxLon=${maxLon}&minLat=${minLat}&maxLat=${maxLat}`,
        {
            ...config,
            tags: { name: 'GetLocationsInExtent' }
        }
    );

    check(res, {
        'get locations in extent success': (r) => r.status === 200,
        'has valid locations array': (r) => Array.isArray(JSON.parse(r.body))
    });
}

function performDeleteLocation(config, session) {
    if (session.createdLocations.length === 0) {
        return;
    }

    const locationId = session.createdLocations.pop();
    const res = http.del(
        `${GEO_URL}/api/locations/${locationId}`,
        null,
        {
            ...config,
            tags: { name: 'DeleteLocation' }
        }
    );

    check(res, {
        'delete location success': (r) => r.status === 204
    });
}

function performUnauthorizedAccess() {
    const endpoints = [
        { method: 'post', url: `${GEO_URL}/api/locations` },
        { method: 'get', url: `${GEO_URL}/api/locations/${randomString(8)}` },
        { method: 'put', url: `${GEO_URL}/api/locations/${randomString(8)}/position` },
        { method: 'delete', url: `${GEO_URL}/api/locations/${randomString(8)}` },
        { method: 'get', url: `${GEO_URL}/api/locations?minLon=0&maxLon=10&minLat=0&maxLat=10` }
    ];

    const endpoint = endpoints[Math.floor(Math.random() * endpoints.length)];
    const payload = endpoint.method !== 'get' ? JSON.stringify(randomCoordinates()) : null;

    const res = http.request(
        endpoint.method,
        endpoint.url,
        payload,
        {
            headers: { 'Content-Type': 'application/json' },
            tags: { name: 'UnauthorizedAccess' }
        }
    );

    check(res, {
        'unauthorized access handled': (r) => r.status === 401
    });
}