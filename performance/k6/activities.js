import http from 'k6/http';
import { check, sleep } from 'k6';
import { generateActivityData } from './utils.js';
import { config } from './config.js';
import { metrics } from './metrics.js';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Create a new activity
export function createActivity(requestConfig, session) {
    const activityData = generateActivityData();

    const res = http.post(
        `${config.ACTIVITY_URL}/api/activity`,
        JSON.stringify(activityData),
        {
            ...requestConfig,
            tags: { name: 'CreateActivity' }
        }
    );

    metrics.activityLatencies.add(res.timings.duration);

    if (res.status !== 201) {
        metrics.logRequestError('CreateActivity', res);
    }

    if (check(res, {
        'create activity success': (r) => r.status === 201,
        'has activity id': (r) => JSON.parse(r.body)?.activityId !== undefined
    })) {
        const activityId = JSON.parse(res.body).activityId;
        if (!session.createdActivities) {
            session.createdActivities = {
                all: [],
                ready: []
            };
        }
        session.createdActivities.all.push(activityId);
        metrics.activeActivities.add(1);
        return activityId;
    } else {
        metrics.activityErrors.add(1);
        return null;
    }
}

// Get an activity
export function getActivity(requestConfig, session) {
    if (!session.createdActivities || !session.createdActivities.all.length) {
        return createActivity(requestConfig, session);
    }

    // Get a random activity ID
    const activityIndex = Math.floor(Math.random() * session.createdActivities.all.length);
    const activityId = session.createdActivities.all[activityIndex];

    let attempts = 0;
    const maxAttempts = 3;

    while (attempts < maxAttempts) {
        const res = http.get(
            `${config.ACTIVITY_URL}/api/activity/${activityId}`,
            {
                ...requestConfig,
                tags: { name: 'GetActivity' },
                validateResponseStatus: false
            }
        );

        metrics.activityLatencies.add(res.timings.duration);
        attempts++;

        if (res.status !== 200 && res.status !== 404) {
            metrics.logRequestError('GetActivity', res);
        }

        if (res.status === 200) {
            // Success - mark as ready
            if (!session.createdActivities.ready) {
                session.createdActivities.ready = [];
            }
            if (!session.createdActivities.ready.includes(activityId)) {
                session.createdActivities.ready.push(activityId);
            }
            return res;
        } else if (res.status === 404 && attempts < maxAttempts) {
            // Activity not ready - wait and retry
            sleep(1); // Wait 1 second before retry
        } else {
            // Other error or max attempts reached
            metrics.activityErrors.add(1);
            break;
        }
    }

    return null;
}

// Edit an activity
export function editActivity(requestConfig, session) {
    if (!session.createdActivities || !session.createdActivities.ready || !session.createdActivities.ready.length) {
        // If no ready IDs, try to get one
        getActivity(requestConfig, session);
        return null;
    }

    const activityIndex = Math.floor(Math.random() * session.createdActivities.ready.length);
    const activityId = session.createdActivities.ready[activityIndex];

    const updateData = {
        name: `Updated Activity ${randomString(8)}`,
        description: `Updated description ${randomString(16)}`,
        icon: `icon-updated-${randomString(4)}`
    };

    const res = http.patch(
        `${config.ACTIVITY_URL}/api/activity/${activityId}`,
        JSON.stringify(updateData),
        {
            ...requestConfig,
            tags: { name: 'EditActivity' }
        }
    );

    if (res.status !== 200) {
        metrics.logRequestError('EditActivity', res);
    }

    metrics.activityLatencies.add(res.timings.duration);

    if (!check(res, {
        'edit activity success': (r) => r.status === 200
    })) {
        metrics.activityErrors.add(1);
        // If we get an unauthorized response, remove from ready list
        if (res.status === 401 || res.status === 404) {
            session.createdActivities.ready = session.createdActivities.ready
                .filter(id => id !== activityId);
            session.createdActivities.all = session.createdActivities.all
                .filter(id => id !== activityId);
        }
    }

    return res;
}

// Delete an activity
export function deleteActivity(requestConfig, session) {
    if (!session.createdActivities || !session.createdActivities.ready || !session.createdActivities.ready.length) {
        return null;
    }

    const activityId = session.createdActivities.ready.pop();
    // Remove from main IDs array too
    session.createdActivities.all = session.createdActivities.all
        .filter(id => id !== activityId);

    const res = http.del(
        `${config.ACTIVITY_URL}/api/activity/${activityId}`,
        null,
        {
            ...requestConfig,
            tags: { name: 'DeleteActivity' }
        }
    );

    metrics.activityLatencies.add(res.timings.duration);

    if (res.status !== 200) {
        metrics.logRequestError('DeleteActivity', res);
    }

    if (check(res, {
        'delete activity success': (r) => r.status === 200
    })) {
        metrics.activeActivities.add(-1);
    } else {
        metrics.activityErrors.add(1);
        // If delete failed and it wasn't a 404, put the ID back
        if (res.status !== 404) {
            session.createdActivities.ready.push(activityId);
            session.createdActivities.all.push(activityId);
        }
    }

    return res;
}