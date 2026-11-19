FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# Java for the Android build tools, and the Android SDK itself (platform 37 is what net11.0-android builds
# against). The Android SDK is installed with Google's sdkmanager: the installer inside the .NET workload
# does not work in a container.
ENV ANDROID_HOME=/opt/android-sdk
ENV ANDROID_SDK_ROOT=/opt/android-sdk
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends openjdk-17-jdk-headless curl unzip \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /opt/android-sdk/cmdline-tools \
    && curl -fsSL -o /tmp/tools.zip https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip \
    && unzip -q /tmp/tools.zip -d /opt/android-sdk/cmdline-tools \
    && mv /opt/android-sdk/cmdline-tools/cmdline-tools /opt/android-sdk/cmdline-tools/latest \
    && rm /tmp/tools.zip \
    && yes | /opt/android-sdk/cmdline-tools/latest/bin/sdkmanager --licenses > /dev/null \
    && /opt/android-sdk/cmdline-tools/latest/bin/sdkmanager "platform-tools" "platforms;android-37.0" "build-tools;36.0.0" > /dev/null \
    && chown -R vscode:vscode /opt/android-sdk
