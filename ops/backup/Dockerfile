FROM alpine:3.22

RUN apk add --no-cache ca-certificates restic sqlite

COPY backup.sh /usr/local/bin/pericias-backup
RUN chmod 0755 /usr/local/bin/pericias-backup

ENTRYPOINT ["/usr/local/bin/pericias-backup"]
