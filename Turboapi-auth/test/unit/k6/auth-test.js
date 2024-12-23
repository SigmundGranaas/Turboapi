import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Create a shared array for storing registered users
let testUsers = new SharedArray('test users', function() {
    return Array.from({ length: 10 }, (_, i) => ({
        email: `testuser${i}@example.com`,
        password: 'TestPassword123!'
    }));
});

// Configuration for different test scenarios
export const options = {
    scenarios: {
        // Smoke test
        smoke: {
            executor: 'constant-vus',
            vus: 1,
            duration: '30s',
            tags: { test_type: 'smoke' },
        },
        // Load test
        load: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                { duration: '2m', target: 50 },  // Ramp up
                { duration: '5m', target: 50 },  // Stay at 50 users
                { duration: '2m', target: 0 },   // Ramp down
            ],
            tags: { test_type: 'load' },
        },
        // Stress test
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
        'http_req_duration{name:RegisterEndpoint}': ['p(95)<500'],
        'http_req_duration{name:LoginEndpoint}': ['p(95)<400'],    // Login should be faster
        'http_req_duration{name:RefreshEndpoint}': ['p(95)<300'],  // Refresh should be fastest
        http_req_failed: ['rate<0.01'],    // Less than 1% can fail
    },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5098';


export function setup() {
    const verifiedUsers = [];

    console.log('Starting setup phase...');

    testUsers.forEach((user, index) => {
        console.log(`Processing user ${index}: ${user.email}`);

        // Try logging in first
        const loginPayload = JSON.stringify({
            email: user.email,
            password: user.password
        });

        const loginResponse = http.post(`${BASE_URL}/api/auth/login`, loginPayload, {
            headers: { 'Content-Type': 'application/json' },
        });

        console.log(`Login attempt for ${user.email}: status ${loginResponse.status}`);
        if (loginResponse.status === 200) {
            console.log(`User ${user.email} verified through login`);
            verifiedUsers.push(user);
        } else {
            // User doesn't exist, try registering
            const registerPayload = JSON.stringify({
                email: user.email,
                password: user.password,
                confirmPassword: user.password
            });

            const registerResponse = http.post(`${BASE_URL}/api/auth/register`, registerPayload, {
                headers: { 'Content-Type': 'application/json' },
            });

            console.log(`Registration attempt for ${user.email}: status ${registerResponse.status}`);
            if (registerResponse.status === 200) {
                console.log(`User ${user.email} registered successfully`);
                verifiedUsers.push(user);
            } else {
                console.log(`Failed to process user ${user.email}. Registration response:`, registerResponse.body);
            }
        }

        // Add a small delay between operations
        sleep(0.5);
    });

    console.log(`Setup complete. ${verifiedUsers.length} users available for testing`);
    return { registeredUsers: verifiedUsers };
}

export default function (data) {
    const rand = Math.random();

    if (rand < 0.4) {
        performRegistration();
    } else if (rand < 0.8 && data.registeredUsers && data.registeredUsers.length > 0) {
        performLogin(data.registeredUsers);
    } else if (global.tokens && global.tokens.length > 0) {
        performTokenRefresh();
    } else {
        // Fallback to registration if no users are available
        performRegistration();
    }

    sleep(1);
}

function performRegistration() {
    const payload = JSON.stringify({
        email: `user_${randomString(8)}@example.com`,
        password: 'TestPassword123!',
        confirmPassword: 'TestPassword123!'
    });

    const res = http.post(`${BASE_URL}/api/auth/register`, payload, {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'RegisterEndpoint' },
    });

    check(res, {
        'register success': (r) => r.status === 200,
        'has tokens': (r) => {
            const body = JSON.parse(r.body);
            return body.accessToken && body.refreshToken;
        },
    });
}


function performLogin(registeredUsers) {
    const user = registeredUsers[Math.floor(Math.random() * registeredUsers.length)];

    const payload = JSON.stringify({
        email: user.email,
        password: user.password
    });

    const res = http.post(`${BASE_URL}/api/auth/login`, payload, {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'LoginEndpoint' },
    });

    const success = check(res, {
        'login success': (r) => r.status === 200,
        'has valid tokens': (r) => {
            const body = JSON.parse(r.body);
            return body.accessToken && body.refreshToken;
        },
    });

    if (success) {
        const body = JSON.parse(res.body);
        if (__ITER % 5 === 0) {
            let tokens = global.tokens || [];
            tokens.push(body.refreshToken);
            if (tokens.length > 50) tokens.shift();
            global.tokens = tokens;
        }
    }
}
function performTokenRefresh() {
    // Skip if no refresh tokens are available
    if (!global.tokens || !global.tokens.length) {
        console.log('No refresh tokens available, skipping refresh test');
        return;
    }

    // Randomly select a stored refresh token
    const refreshToken = global.tokens[Math.floor(Math.random() * global.tokens.length)];

    const payload = JSON.stringify({
        refreshToken: refreshToken
    });

    const res = http.post(`${BASE_URL}/api/auth/refresh`, payload, {
        headers: { 'Content-Type': 'application/json' },
        tags: { name: 'RefreshEndpoint' },
    });

    check(res, {
        'refresh success': (r) => r.status === 200,
        'has new tokens': (r) => {
            const body = JSON.parse(r.body);
            return body.accessToken && body.refreshToken;
        },
    });

    // Update stored refresh token if successful
    if (res.status === 200) {
        const body = JSON.parse(res.body);
        const tokenIndex = global.tokens.indexOf(refreshToken);
        if (tokenIndex !== -1) {
            global.tokens[tokenIndex] = body.refreshToken;
        }
    }
}