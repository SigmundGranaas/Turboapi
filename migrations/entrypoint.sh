#!/bin/sh
# Simple entrypoint script with proper env variable expansion
flyway -url=jdbc:postgresql://${DB_HOST}:${DB_PORT}/${DB_NAME} \
-user=${DB_USER} \
-password=${DB_PASSWORD} \
-locations=filesystem:/flyway/sql \
-baselineOnMigrate=true \
migrate