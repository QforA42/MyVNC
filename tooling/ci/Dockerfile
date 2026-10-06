# Local CI image: everything `gate.mjs ci --strict` needs, so the full CI runs on the developer machine
# (tooling/scripts/ci-local.mjs) and on self-hosted Gitea runners with the same tools.
# Renovate pins the image tags below by digest (pinDigests in .github/renovate.json).
# NODE_VERSION comes from the repository's .nvmrc (ci-local.mjs passes it).
ARG NODE_VERSION=22
FROM ghcr.io/gitleaks/gitleaks:latest AS gitleaks
FROM ghcr.io/google/osv-scanner:latest AS osv
FROM rhysd/actionlint:latest AS actionlint
FROM hadolint/hadolint:latest-debian AS hadolint
FROM aquasec/trivy:latest AS trivy
FROM docker:cli AS dockercli

FROM node:${NODE_VERSION}-bookworm-slim
# .NET repositories: ci-local.mjs builds with --build-arg WITH_DOTNET=true when the dotnetDir folder (vars in
# tooling/gates.yaml) has global.json or Directory.Build.props.
ARG WITH_DOTNET=false
ARG DOTNET_CHANNEL=LTS
RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates curl openssl \
 && rm -rf /var/lib/apt/lists/* \
 && corepack enable \
 && git config --system --add safe.directory '*'
RUN if [ "$WITH_DOTNET" = "true" ]; then \
      apt-get update && apt-get install -y --no-install-recommends libicu72 && rm -rf /var/lib/apt/lists/* \
      && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
      && bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir /usr/share/dotnet \
      && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet && rm /tmp/dotnet-install.sh; \
    fi
COPY --from=gitleaks /usr/bin/gitleaks /usr/local/bin/gitleaks
COPY --from=osv /osv-scanner /usr/local/bin/osv-scanner
COPY --from=actionlint /usr/local/bin/actionlint /usr/local/bin/actionlint
COPY --from=hadolint /bin/hadolint /usr/local/bin/hadolint
COPY --from=trivy /usr/local/bin/trivy /usr/local/bin/trivy
# Docker CLI and compose plugin for ci-local.mjs --docker (integration tests against the host engine).
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
ENV CI=true COREPACK_ENABLE_DOWNLOAD_PROMPT=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /work
# Copied-code detection for the duplication gate.
RUN npm install -g jscpd@4 --no-audit --no-fund
COPY entrypoint.sh /usr/local/bin/ci-entrypoint
ENTRYPOINT ["sh", "/usr/local/bin/ci-entrypoint"]
CMD ["ci"]
