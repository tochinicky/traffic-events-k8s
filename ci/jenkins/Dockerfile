# Jenkins controller with the pipeline's toolchain baked in: .NET SDK, docker CLI, kind, kubectl.
# Local demo only — a real setup would run builds on agents, not the controller.
FROM docker:28-cli AS docker-cli

FROM jenkins/jenkins:lts-jdk21
USER root

ARG TARGETARCH
ARG KIND_VERSION=v0.33.0
ARG KUBECTL_VERSION=v1.37.1

# docker CLI + buildx plugin (the Dockerfile uses BuildKit cache mounts).
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-buildx /usr/local/libexec/docker/cli-plugins/docker-buildx

RUN curl -fsSLo /usr/local/bin/kind "https://kind.sigs.k8s.io/dl/${KIND_VERSION}/kind-linux-${TARGETARCH}" \
 && curl -fsSLo /usr/local/bin/kubectl "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${TARGETARCH}/kubectl" \
 && chmod +x /usr/local/bin/kind /usr/local/bin/kubectl

# .NET 10 SDK (the SDK needs ICU unless globalization-invariant mode is on).
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PATH="/usr/share/dotnet:${PATH}"
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --version

# Plugins + configuration-as-code: no setup wizard, admin user, credential and job all declared.
COPY plugins.txt /usr/share/jenkins/ref/plugins.txt
RUN jenkins-plugin-cli --plugin-file /usr/share/jenkins/ref/plugins.txt
COPY casc.yaml /var/jenkins_home/casc.yaml
ENV CASC_JENKINS_CONFIG=/var/jenkins_home/casc.yaml \
    JAVA_OPTS="-Djenkins.install.runSetupWizard=false -Dhudson.plugins.git.GitSCM.ALLOW_LOCAL_CHECKOUT=true -Xmx768m"
