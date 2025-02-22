import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { Counter, Trend } from 'k6/metrics';

// Custom metrics
const activeActivities = new Counter('active_activities');
const activityLatencies = new Trend('activity_latencies');
const activityErrors = new Counter('activity_errors');

const sessionActivityIds = {};
const sessionReadyActivityIds = {};

// Add these with other custom metrics
const errorsByType = new Counter('errors_by_type');
const errorsByStatusCode = new Counter('errors_by_status_code');

// Add this helper function
function logRequestError(operation, res) {
    const errorKey = `${operation}_${res.status}`;
    errorsByType.add(1, { operation });
    errorsByStatusCode.add(1, { status: res.status });

    console.log(`${operation} failed:`, {
        status: res.status,
        body: res.body,
        timings: res.timings,
        url: res.url
    });
}


// Store test users
const testUsers = new SharedArray('test users', function() {
    return Array.from({ length: 10 }, (_, i) => ({
        email: `activitytester${i}@example.com`,
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
        'http_req_duration{name:CreateActivity}': ['p(95)<500'],
        'http_req_duration{name:EditActivity}': ['p(95)<400'],
        'http_req_duration{name:GetActivity}': ['p(95)<300'],
        'http_req_duration{name:DeleteActivity}': ['p(95)<400'],
        'http_req_failed': ['rate<0.01'],
        'active_activities': ['count>0'],
        'activity_latencies': ['p(95)<500'],
        'activity_errors': ['count<100'],
    },
};

const AUTH_URL = 'http://localhost:5098';
const ACTIVITY_URL = 'http://localhost:5107';
const GEO_URL = 'http://localhost:5028';

function generateActivityData() {
    return {
        position: {
            latitude: Math.random() * 180 - 90,
            longitude: Math.random() * 360 - 180
        },
        name: `Test Activity ${randomString(8)}`,
        description: `Description for test activity ${randomString(16)}`,
        icon: `icon-${randomString(4)}`
    };
}

function authenticateUser(user) {
    const loginPayload = JSON.stringify({
        email: user.email,
        password: user.password
    });

    const loginRes = http.post(`${AUTH_URL}/api/auth/login`, loginPayload, {
        headers: { 'Content-Type': 'application/json' }
    });

    if (loginRes.status === 200) {
        return { token: JSON.parse(loginRes.body).accessToken };
    }

    const registerPayload = JSON.stringify({
        email: user.email,
        password: user.password,
        confirmPassword: user.password
    });

    const registerRes = http.post(`${AUTH_URL}/api/auth/register`, registerPayload, {
        headers: { 'Content-Type': 'application/json' }
    });

    if (registerRes.status === 200) {
        const loginAfterRegister = http.post(`${AUTH_URL}/api/auth/login`, loginPayload, {
            headers: { 'Content-Type': 'application/json' }
        });

        if (loginAfterRegister.status === 200) {
            return { token: JSON.parse(loginAfterRegister.body).accessToken };
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
                token: session.token
            };
        }
        return null;
    }).filter(session => session !== null);

    console.log(`Setup complete. ${sessions.length} users authenticated.`);
    return { sessions };
}

function performCreateActivity(config, session) {
    const activityData = generateActivityData();
    const res = http.post(
        `${ACTIVITY_URL}/api/activity`,
        JSON.stringify(activityData),
        {
            ...config,
            tags: { name: 'CreateActivity' }
        }
    );

    activityLatencies.add(res.timings.duration);

    if (res.status !== 201) {
        logRequestError('CreateActivity', res);
    }

    if (check(res, {
        'create activity success': (r) => r.status === 201,
        'has activity id': (r) => JSON.parse(r.body).activityId !== undefined
    })) {
        const activityId = JSON.parse(res.body).activityId;
        if (!sessionActivityIds[session.token]) {
            sessionActivityIds[session.token] = [];
            sessionReadyActivityIds[session.token] = [];
        }
        sessionActivityIds[session.token].push(activityId);
        activeActivities.add(1);
        return activityId;
    } else {
        activityErrors.add(1);
        return null;
    }
}

function performGetActivity(config, session) {
    if (!sessionActivityIds[session.token] || !sessionActivityIds[session.token].length) {
        performCreateActivity(config, session);
        return;
    }

    // Get a random activity ID
    const activityId = sessionActivityIds[session.token][
        Math.floor(Math.random() * sessionActivityIds[session.token].length)
        ];

    let attempts = 0;
    const maxAttempts = 3;

    while (attempts < maxAttempts) {
        const res = http.get(
            `${ACTIVITY_URL}/api/activity/${activityId}`,
            {
                ...config,
                tags: { name: 'GetActivity' },
                validateResponseStatus: false
            }
        );

        activityLatencies.add(res.timings.duration);
        attempts++;

        if (res.status !== 200 && res.status !== 404) {
            logRequestError('GetActivity', res);
        }

        if (res.status === 200) {
            // Success - mark as ready and return
            if (!sessionReadyActivityIds[session.token]) {
                sessionReadyActivityIds[session.token] = [];
            }
            if (!sessionReadyActivityIds[session.token].includes(activityId)) {
                sessionReadyActivityIds[session.token].push(activityId);
            }
            return;
        } else if (res.status === 404 && attempts < maxAttempts) {
            // Activity not ready - wait and retry
            retryAttempts.add(1);
            sleep(1); // Wait 1 second before retry
        } else {
            // Other error or max attempts reached
            activityErrors.add(1);
            break;
        }
     
    }
}


function performEditActivity(config, session) {
    if (!sessionReadyActivityIds[session.token] || !sessionReadyActivityIds[session.token].length) {
        // If no ready IDs, try to get one
        performGetActivity(config, session);
        return;
    }

    const activityId = sessionReadyActivityIds[session.token][
        Math.floor(Math.random() * sessionReadyActivityIds[session.token].length)
        ];

    const updateData = {
        name: `Updated Activity ${randomString(8)}`,
        description: `Updated description ${randomString(16)}`,
        icon: `icon-updated-${randomString(4)}`
    };

    const res = http.patch(
        `${ACTIVITY_URL}/api/activity/${activityId}`,
        JSON.stringify(updateData),
        {
            ...config,
            tags: { name: 'EditActivity' }
        }
    );

    if (res.status !== 200) {
        logRequestError('EditActivity', res);
    }

    activityLatencies.add(res.timings.duration);

    if (!check(res, {
        'edit activity success': (r) => r.status === 200
    })) {
        activityErrors.add(1);
        // If we get an unauthorized response, remove from ready list
        if (res.status === 401 || res.status === 404) {
            sessionReadyActivityIds[session.token] = sessionReadyActivityIds[session.token]
                .filter(id => id !== activityId);
            sessionActivityIds[session.token] = sessionActivityIds[session.token]
                .filter(id => id !== activityId);
        }
    }
}

function performDeleteActivity(config, session) {
    if (!sessionReadyActivityIds[session.token] || !sessionReadyActivityIds[session.token].length) {
        return;
    }

    const activityId = sessionReadyActivityIds[session.token].pop();
    // Remove from main IDs array too
    sessionActivityIds[session.token] = sessionActivityIds[session.token]
        .filter(id => id !== activityId);

    const res = http.del(
        `${ACTIVITY_URL}/api/activity/${activityId}`,
        null,
        {
            ...config,
            tags: { name: 'DeleteActivity' }
        }
    );

    activityLatencies.add(res.timings.duration);

    if (res.status !== 200) {
        logRequestError('DeleteActivity', res);
    }

    if (check(res, {
        'delete activity success': (r) => r.status === 200
    })) {
        activeActivities.add(-1);
    } else {
        console.log(res)

        activityErrors.add(1);
        // If delete failed and it wasn't a 404, put the ID back
        if (res.status !== 404) {
            sessionReadyActivityIds[session.token].push(activityId);
            sessionActivityIds[session.token].push(activityId);
        }
    }
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
    const currentActive = activeActivities.value;

    // Pass session to all operations
    if (currentActive < 10 || rand < 0.4) {
        performCreateActivity(requestConfig, session);
    } else if (rand < 0.6) {
        performGetActivity(requestConfig, session);
    } else if (rand < 0.8) {
        performEditActivity(requestConfig, session);
    } else {
        performDeleteActivity(requestConfig, session);
    }
}