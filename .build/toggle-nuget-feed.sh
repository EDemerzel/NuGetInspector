#!/usr/bin/env bash

# toggle-nuget-feed.sh
# Usage: ./toggle-nuget-feed.sh enable dotnet-experimental dotnet10

set -e

ACTION=$1
shift
SOURCES=("$@")

CONFIG_PATH="./nuget.config"

if [[ ! -f "$CONFIG_PATH" ]]; then
    echo "❌ nuget.config not found at $CONFIG_PATH"
    exit 1
fi

for SOURCE in "${SOURCES[@]}"; do
    if [[ "$ACTION" == "disable" ]]; then
        xmlstarlet ed --inplace \
            -s "/configuration/disabledPackageSources" -t elem -n "add" -v "" \
            -i "/configuration/disabledPackageSources/add[not(@key='$SOURCE')]" -t attr -n "key" -v "$SOURCE" \
            -i "/configuration/disabledPackageSources/add[@key='$SOURCE']" -t attr -n "value" -v "true" \
            "$CONFIG_PATH"
        echo "🔒 Disabled: $SOURCE"
    elif [[ "$ACTION" == "enable" ]]; then
        xmlstarlet ed --inplace \
            -d "/configuration/disabledPackageSources/add[@key='$SOURCE']" \
            "$CONFIG_PATH"
        echo "🔓 Enabled: $SOURCE"
    else
        echo "Usage: $0 [enable|disable] source1 [source2 ...]"
        exit 1
    fi
done

echo "✅ nuget.config updated successfully"
