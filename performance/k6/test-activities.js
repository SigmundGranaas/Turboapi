import { sleep } from 'k6';
import { scenarios } from './scenarios.js';
import { config } from './config.js';
import { createTestUsers } from './utils.js';
import { setupSessions } from './auth.js';
import {
    createActivity,
    getActivity,
    editActivity,
    deleteActivity
} from './activities.js';
import { metrics } from './metrics.js';

// Create test users
const testUsers = createTestUsers('activitytester', config.TEST_USER_COUNT);

// Define thresholds
export const options = {
    scenarios: scenarios,
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

// Setup authenticated sessions
export function setup() {
    return setupSessions(testUsers);
}

// Default function executed for each VU
export default function(data) {
    if (!data.sessions || data.sessions.length === 0) {
        console.log('No authenticated sessions available');
        return;
    }

    // Get a random session
    const sessionIndex = Math.floor(Math.random() * data.sessions.length);
    const session = data.sessions[sessionIndex];

    const requestConfig = {
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${session.token}`
        }
    };

    // Choose a random operation to perform based on current state
    const rand = Math.random();
    const currentActive = metrics.activeActivities.value;

    if (currentActive < 10 || rand < 0.4) {
        createActivity(requestConfig, session);
    } else if (rand < 0.6) {
        getActivity(requestConfig, session);
    } else if (rand < 0.8) {
        editActivity(requestConfig, session);
    } else {
        deleteActivity(requestConfig, session);
    }

    sleep(1);
}
