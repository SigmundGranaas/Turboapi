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
    try {
        // Validate data object
        if (!data) {
            console.log('Data object is null or undefined');
            sleep(1);
            return;
        }

        // Validate sessions array
        if (!data.sessions || !Array.isArray(data.sessions) || data.sessions.length === 0) {
            console.log('No authenticated sessions available');
            sleep(1);
            return;
        }

        // Get a random session
        const sessionIndex = Math.floor(Math.random() * data.sessions.length);
        const session = data.sessions[sessionIndex];

        // Validate session object
        if (!session) {
            console.log('Selected session is null or undefined');
            sleep(1);
            return;
        }

        // Validate token
        if (!session.token) {
            console.log('Session token is missing');
            sleep(1);
            return;
        }

        // Initialize createdActivities if missing
        if (!session.createdActivities) {
            console.log('Initializing missing createdActivities in session');
            session.createdActivities = {
                all: [],
                ready: []
            };
        }

        // Validate arrays
        if (!Array.isArray(session.createdActivities.all)) {
            console.log('createdActivities.all is not an array, fixing');
            session.createdActivities.all = [];
        }

        if (!Array.isArray(session.createdActivities.ready)) {
            console.log('createdActivities.ready is not an array, fixing');
            session.createdActivities.ready = [];
        }

        const requestConfig = {
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${session.token}`
            }
        };

        // Choose a random operation to perform based on current state
        const rand = Math.random();
        const currentActive = metrics.activeActivities.value || 0;

        console.log(`Current active activities: ${currentActive}, random value: ${rand}`);

        if (currentActive < 10 || rand < 0.4) {
            console.log('Performing createActivity operation');
            createActivity(requestConfig, session);
        } else if (rand < 0.6) {
            console.log('Performing getActivity operation');
            getActivity(requestConfig, session);
        } else if (rand < 0.8) {
            console.log('Performing editActivity operation');
            editActivity(requestConfig, session);
        } else {
            console.log('Performing deleteActivity operation');
            deleteActivity(requestConfig, session);
        }

        sleep(1);
    } catch (error) {
        console.log(`Error in default function: ${error.message}`);
        sleep(1);
    }
}
