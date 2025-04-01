FROM flyway/flyway:latest

# Copy SQL scripts
COPY ./Turboapi-auth/db/migrations/ /flyway/sql/

# Create entry point script
RUN echo '#!/bin/sh \n\
flyway -url=jdbc:postgresql://${DB_HOST}:${DB_PORT}/${DB_NAME} \
-user=${DB_USER} -password=${DB_PASSWORD} \
-locations=filesystem:/flyway/sql \
-baselineOnMigrate=true \
migrate' > /entrypoint.sh && \
chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]