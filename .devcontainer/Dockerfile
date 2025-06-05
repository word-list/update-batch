FROM mcr.microsoft.com/dotnet/sdk:8.0

RUN apt-get update && apt-get install -y \
    clang \
    lld \
    libz-dev \    
    libc6-dev-arm64-cross \
    gcc-aarch64-linux-gnu \
    ssh-client \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace