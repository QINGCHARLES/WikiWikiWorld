#!/usr/bin/env bash
set -euo pipefail

# Manually download NuGet packages using wget (which works with the JWT proxy)
# and place them in the NuGet global cache

CACHE_DIR="${HOME}/.nuget/packages"
mkdir -p "${CACHE_DIR}"

# Function to download and extract a NuGet package
download_package() {
    local name=$1
    local version=$2
    local name_lower=$(echo "$name" | awk '{print tolower($0)}')

    # Skip if already exists
    if [ -d "${CACHE_DIR}/${name_lower}/${version}" ]; then
        echo "[download] Skipping ${name} ${version} (already cached)"
        return 0
    fi

    echo "[download] Downloading ${name} ${version}..."

    # Download package
    wget -q "https://www.nuget.org/api/v2/package/${name}/${version}" \
        -O "/tmp/${name}.${version}.nupkg" || return 1

    # Create package directory
    mkdir -p "${CACHE_DIR}/${name_lower}/${version}"

    # Extract package
    cd "${CACHE_DIR}/${name_lower}/${version}"
    unzip -q "/tmp/${name}.${version}.nupkg" || return 1

    # Create the .nupkg.metadata file
    cat > "${name_lower}.${version}.nupkg.sha512" <<EOF
$(sha512sum "/tmp/${name}.${version}.nupkg" | cut -d' ' -f1)
EOF

    # Copy the nupkg file
    cp "/tmp/${name}.${version}.nupkg" "${name_lower}.${version}.nupkg"

    echo "[download] Successfully cached ${name} ${version}"
    cd - > /dev/null
}

# Download all required packages
echo "[download] Starting package downloads..."

download_package "Dapper" "2.1.66"
download_package "Microsoft.Data.Sqlite" "9.0.9"
download_package "Microsoft.Data.Sqlite.Core" "9.0.9"
download_package "SQLitePCLRaw.bundle_e_sqlite3" "2.1.10"
download_package "SQLitePCLRaw.core" "2.1.10"
download_package "SQLitePCLRaw.lib.e_sqlite3" "2.1.10"
download_package "SQLitePCLRaw.provider.e_sqlite3" "2.1.10"
download_package "AngleSharp" "1.3.0"
download_package "Markdig" "0.42.0"
download_package "Microsoft.AspNetCore.Authentication.JwtBearer" "9.0.9"
download_package "Microsoft.Extensions.Hosting.Systemd" "9.0.9"
download_package "Microsoft.IdentityModel.Abstractions" "8.4.0"
download_package "Microsoft.IdentityModel.JsonWebTokens" "8.4.0"
download_package "Microsoft.IdentityModel.Logging" "8.4.0"
download_package "Microsoft.IdentityModel.Protocols" "8.4.0"
download_package "Microsoft.IdentityModel.Protocols.OpenIdConnect" "8.4.0"
download_package "Microsoft.IdentityModel.Tokens" "8.4.0"
download_package "System.IdentityModel.Tokens.Jwt" "8.4.0"
download_package "System.Text.Encodings.Web" "9.0.1"

# Transitive dependencies
download_package "System.Memory" "4.5.5"
download_package "Microsoft.Extensions.Hosting" "9.0.1"
download_package "Microsoft.Extensions.Logging.Abstractions" "9.0.0"
download_package "Microsoft.Extensions.Configuration.Abstractions" "9.0.0"
download_package "Microsoft.Extensions.DependencyInjection" "9.0.0"
download_package "Microsoft.Extensions.DependencyInjection.Abstractions" "9.0.0"
download_package "Microsoft.Extensions.Diagnostics" "9.0.0"
download_package "Microsoft.Extensions.FileProviders.Abstractions" "9.0.0"
download_package "Microsoft.Extensions.Hosting.Abstractions" "9.0.0"
download_package "Microsoft.Extensions.Logging" "9.0.0"
download_package "Microsoft.Extensions.Options" "9.0.0"
download_package "Microsoft.Extensions.Options.ConfigurationExtensions" "9.0.0"
download_package "Microsoft.Extensions.Primitives" "9.0.0"

echo "[download] Package downloads complete!"
echo "[download] You can now run: dotnet restore --source ${CACHE_DIR}"
