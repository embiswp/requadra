FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# What the MonoGame window needs to run in a container with no graphics card: software OpenGL (Mesa),
# the X libraries SDL2 loads, a virtual display for smoke runs without a screen (xvfb), and OpenAL.
# SDL2 and OpenAL themselves come with the MonoGame.Framework.DesktopGL package.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        libgl1 libgl1-mesa-dri libglu1-mesa libgbm1 libdrm2 \
        libx11-6 libxext6 libxcursor1 libxi6 libxrandr2 libxinerama1 libxss1 \
        libasound2t64 libopenal1 \
        xvfb x11-utils \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
