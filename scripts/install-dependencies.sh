#!/usr/bin/env bash
###############################################################################
# install-dependencies.sh
#
# Installs everything needed to build and run KRocketDocumentScanner:
#   - .NET 10 SDK
#   - SANE (scanner backend/utilities, needed by NAPS2.Sdk at runtime)
#   - sane-airscan (SANE backend for network scanners that speak AirScan/eSCL —
#     the app lists and uses these through SANE)
#   - avahi + mDNS name lookup (network scanner discovery, and resolving
#     names like printer.local)
#   - fontconfig and the basic X11 libraries (Avalonia/SkiaSharp text and windows)
#
# The tests (tests/KRocketDocumentScanner.Tests) need nothing beyond the .NET SDK and network
# access to nuget.org to restore packages; the hardware tests need a scanner.
#
# Detects the host Linux distribution and uses the appropriate package
# manager: apt (Debian/Ubuntu-family) or dnf (Fedora/RHEL-family). This is
# strictly a dependency installer — it does NOT build, package, or install
# KRocketDocumentScanner itself.
#
# Usage:
#   ./install-dependencies.sh
#
# Must be run with sudo privileges available (the script calls `sudo`
# itself for package-manager commands; you don't need to prefix the whole
# script with sudo).
###############################################################################
set -euo pipefail

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
log()  { printf '\033[1;34m==>\033[0m %s\n' "$1"; }
warn() { printf '\033[1;33m==> WARNING:\033[0m %s\n' "$1" >&2; }
die()  {
    printf '\033[1;31m==> ERROR:\033[0m %s\n' "$1" >&2
    exit 1
}

require_sudo() {
    if [ "$(id -u)" -eq 0 ]; then
        SUDO=""
    elif command -v sudo >/dev/null 2>&1; then
        SUDO="sudo"
    else
        die "This script needs root privileges to install system packages, but neither running as root nor 'sudo' is available. Re-run as root, or install 'sudo' first."
    fi
}

# ---------------------------------------------------------------------------
# Step 1: Detect distro family and package manager
# ---------------------------------------------------------------------------
detect_package_manager() {
    if [ ! -f /etc/os-release ]; then
        die "Could not find /etc/os-release, so the Linux distribution couldn't be detected. This script only supports apt-based (Debian/Ubuntu) and dnf-based (Fedora/RHEL) systems."
    fi

    # shellcheck source=/dev/null
    . /etc/os-release
    local id="${ID:-unknown}"
    local id_like="${ID_LIKE:-}"

    if [ "$id" = "ubuntu" ] || [ "$id" = "debian" ] || [[ "$id_like" == *"debian"* ]]; then
        echo "apt"
    elif [ "$id" = "fedora" ] || [ "$id" = "rhel" ] || [ "$id" = "centos" ] || [[ "$id_like" == *"fedora"* ]] || [[ "$id_like" == *"rhel"* ]]; then
        echo "dnf"
    else
        die "Unrecognized or unsupported Linux distribution ('${PRETTY_NAME:-$id}'). This script only supports apt-based (Debian/Ubuntu) and dnf-based (Fedora/RHEL/CentOS) systems. You'll need to install the .NET 10 SDK and SANE manually for this distribution — see https://learn.microsoft.com/dotnet/core/install/linux for .NET."
    fi
}

# ---------------------------------------------------------------------------
# Step 2: Install .NET SDK
# ---------------------------------------------------------------------------
install_dotnet_apt() {
    log "Installing .NET 10 SDK via apt..."

    # Don't treat `apt-get update` failures as fatal: a single broken third-party repo
    # (a stray PPA, an expired signing key, etc.) makes `apt-get update` exit non-zero
    # even though every OTHER configured repository updated fine. The package we need
    # may still be perfectly installable from those, so we warn and continue rather
    # than jumping straight to the fallback installer over an unrelated repo's problem.
    if ! $SUDO apt-get update -qq; then
        warn "apt-get update reported errors (often caused by an unrelated third-party repository, not the ones we need) — continuing anyway."
    fi

    if $SUDO apt-get install -y dotnet-sdk-10.0; then
        return 0
    fi

    warn "dotnet-sdk-10.0 was not available via apt on this system (common on older Ubuntu/Debian releases whose default repos don't carry it)."
    install_dotnet_via_official_script
}

install_dotnet_dnf() {
    log "Installing .NET 10 SDK via dnf..."
    if $SUDO dnf install -y dotnet-sdk-10.0; then
        return 0
    fi

    warn "dotnet-sdk-10.0 was not available via dnf on this system."
    install_dotnet_via_official_script
}

install_dotnet_via_official_script() {
    log "Falling back to Microsoft's official install script (installs to \$HOME/.dotnet, no root needed)..."
    if ! command -v curl >/dev/null 2>&1; then
        die "The fallback .NET installer needs 'curl', which isn't installed. Install curl and re-run this script, or install .NET manually: https://learn.microsoft.com/dotnet/core/install/linux"
    fi

    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
        || die "Could not download the .NET install script from https://dot.net/v1/dotnet-install.sh — check your network connection."

    bash /tmp/dotnet-install.sh --channel 10.0 \
        || die "The .NET install script failed. See the output above for details."
    rm -f /tmp/dotnet-install.sh

    warn "Installed .NET 10 SDK to \$HOME/.dotnet — add this to your shell profile if not already present:"
    warn '  export DOTNET_ROOT=$HOME/.dotnet'
    warn '  export PATH=$PATH:$HOME/.dotnet'
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$PATH:$HOME/.dotnet"
}

# ---------------------------------------------------------------------------
# Step 3: Install SANE (scanner backend — used by NAPS2.Sdk) and fontconfig
# ---------------------------------------------------------------------------
install_other_deps_apt() {
    log "Installing SANE, AirScan, avahi/mDNS, fontconfig and X11 libraries via apt..."
    $SUDO apt-get install -y \
        sane-utils libsane1 sane-airscan \
        avahi-daemon libnss-mdns \
        fontconfig libfontconfig1 libx11-6 libice6 libsm6 \
        || die "Failed to install the scanner/font/X11 packages via apt. See the output above."
}

install_other_deps_dnf() {
    log "Installing SANE, AirScan, avahi/mDNS, fontconfig and X11 libraries via dnf..."
    $SUDO dnf install -y \
        sane-backends sane-backends-drivers-scanners sane-airscan \
        avahi nss-mdns \
        fontconfig libX11 libICE libSM \
        || die "Failed to install the scanner/font/X11 packages via dnf. See the output above."
}

# ---------------------------------------------------------------------------
# Step 4: Verify
# ---------------------------------------------------------------------------
verify_installation() {
    log "Verifying installation..."

    if command -v dotnet >/dev/null 2>&1; then
        local dotnet_version
        dotnet_version="$(dotnet --version)"
        log "dotnet: $dotnet_version"
        case "$dotnet_version" in
            1[0-9].*|[2-9][0-9].*) ;;
            *) warn "dotnet $dotnet_version is older than 10 — KRocketDocumentScanner targets .NET 10 and will not build with it." ;;
        esac
    else
        warn "dotnet was installed but isn't on PATH in this shell session. Open a new terminal (or 'source' your shell profile) before building KRocketDocumentScanner."
    fi

    if command -v scanimage >/dev/null 2>&1; then
        log "scanimage: available ($(scanimage -V 2>&1 | head -n1))"
    else
        warn "scanimage (SANE utilities) doesn't seem to be on PATH — scanning may not work until this is resolved."
    fi

    # Network scanner discovery goes through avahi (mDNS). The package normally starts it,
    # but this script does not change any service settings, so just report the state.
    if command -v systemctl >/dev/null 2>&1; then
        if systemctl is-active --quiet avahi-daemon 2>/dev/null; then
            log "avahi-daemon: running"
        else
            warn "avahi-daemon is not running — network scanners may not be discovered. Start it with: sudo systemctl enable --now avahi-daemon"
        fi
    fi
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
main() {
    log "KRocketDocumentScanner dependency installer"

    require_sudo
    local pkg_mgr
    pkg_mgr="$(detect_package_manager)"
    log "Detected package manager: $pkg_mgr"

    case "$pkg_mgr" in
        apt)
            install_dotnet_apt
            install_other_deps_apt
            ;;
        dnf)
            install_dotnet_dnf
            install_other_deps_dnf
            ;;
        *)
            die "Internal error: unrecognized package manager '$pkg_mgr'."
            ;;
    esac

    verify_installation
    log "Done. You should now be able to run 'dotnet build' / 'dotnet restore' in the KRocketDocumentScanner project."
}

main "$@"
