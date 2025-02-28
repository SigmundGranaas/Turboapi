import { scenarios } from './scenarios.js';
import { sleep } from 'k6';
import { config } from './config.js';

// Import all test modules
import * as authTest from './test-auth.js';
import * as locationsTest from './test-locations.js';
import * as activitiesTest from './test-activities.js';

// Combine all thresholds
export const options = {
    scenarios: {
        auth: {
            ...scenarios.smoke,
            exec: 'runAuthTests',
            tags: { service: 'auth', ...scenarios.smoke.tags }
        },
        locations: {
            ...scenarios.smoke,
            exec: 'runLocationTests',
            tags: { service: 'locations', ...scenarios.smoke.tags }
        },
        activities: {
            ...scenarios.smoke,
            exec: 'runActivityTests',
            tags: { service: 'activities', ...scenarios.smoke.tags }
        },
    },
    thresholds: {
        // Combined thresholds from all tests
        ...authTest.options.thresholds,
        ...locationsTest.options.thresholds,
        ...activitiesTest.options.thresholds,
    },
};

// Setup for all tests
export function setup() {
    return {
        auth: authTest.setup(),
        locations: locationsTest.setup(),
        activities: activitiesTest.setup()
    };
}

// Auth test executor
export function runAuthTests(data) {
    authTest.default(data.auth);
    sleep(1);
}

// Locations test executor
export function runLocationTests(data) {
    locationsTest.default(data.locations);
    sleep(1);
}

// Activities test executor
export function runActivityTests(data) {
    activitiesTest.default(data.activities);
    sleep(1);
}