# Simple static site served by nginx
FROM nginx:1.27-alpine

# Copy static assets
COPY index.html /usr/share/nginx/html/index.html
COPY styles.css /usr/share/nginx/html/styles.css

# Provide a default nginx config that supports SPA and gzip/static
RUN rm -f /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
