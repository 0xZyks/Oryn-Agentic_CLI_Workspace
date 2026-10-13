#!/usr/bin/env bash
# Build and install for the current Linux user; run again after source updates.
set -euo pipefail

fail() { printf 'oryn: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'EOF'
Usage: bash scripts/install-linux.sh [--prefix DIR] [--runtime RID] [--no-path]

Requires a .NET 10 SDK and network access for dependencies/runtime packs.
Installs to ~/.local/share/oryn, links ~/.local/bin/oryn, and adds the bin
directory to Bash/Zsh profiles without duplicate entries.
--prefix DIR   Use DIR/share/oryn and DIR/bin instead of ~/.local.
--runtime RID  linux-x64, linux-arm64, linux-musl-x64 or linux-musl-arm64.
               Defaults to the host architecture with glibc (not Alpine).
--no-path      Leave shell configuration unchanged.
EOF
}

prefix="$HOME/.local"
runtime=''
update_path=true
while (($#)); do
    case "$1" in
        --prefix|--runtime)
            (($# >= 2)) || fail "$1 requires a value"
            if [[ "$1" == --prefix ]]; then prefix="$2"; else runtime="$2"; fi
            shift 2 ;;
        --no-path) update_path=false; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ $(uname -s) == Linux ]] || fail 'Run this installer on Linux.'
command -v dotnet >/dev/null || fail 'Install a .NET 10 SDK first.'
if [[ -z "$runtime" ]]; then
    case "$(uname -m)" in
        x86_64) runtime=linux-x64 ;;
        aarch64|arm64) runtime=linux-arm64 ;;
        *) fail 'Unsupported architecture; use an explicit --runtime.' ;;
    esac
fi
case "$runtime" in
    linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64) ;;
    *) fail "Unsupported Linux runtime: $runtime" ;;
esac

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
mkdir -p -- "$prefix"
prefix=$(cd -- "$prefix" && pwd -P)
install_dir="$prefix/share/oryn"
bin_dir="$prefix/bin"
launcher="$bin_dir/oryn"
if [[ -e "$launcher" || -L "$launcher" ]]; then
    [[ -L "$launcher" && $(readlink -- "$launcher") == "$install_dir/oryn" ]] ||
        fail "$launcher already exists and is not this installer's link."
fi

# Publish first so a failed restore/build leaves an existing installation intact.
publish_dir=$(mktemp -d "${TMPDIR:-/tmp}/oryn-publish.XXXXXX")
trap 'rm -rf -- "$publish_dir"' EXIT
(
    cd -- "$repo_root"
    dotnet publish src/Oryn.Cli/Oryn.Cli.csproj --configuration Release \
        --runtime "$runtime" --self-contained true --output "$publish_dir"
)
[[ -x "$publish_dir/oryn" ]] || fail 'Publication did not produce an executable oryn.'
mkdir -p -- "$install_dir" "$bin_dir"
cp -R -- "$publish_dir/." "$install_dir/"
ln -sfn -- "$install_dir/oryn" "$launcher"

# Single-quote the literal path for POSIX shell profiles, including spaces/apostrophes.
quoted_bin=${bin_dir//\'/\'\\\'\'}
path_line="export PATH='$quoted_bin':\"\$PATH\""
if [[ "$update_path" == true ]]; then
    profiles=("$HOME/.profile" "$HOME/.bashrc" "$HOME/.zshrc")
    # Do not create a Bash login profile that would mask the existing .profile.
    for login_profile in "$HOME/.bash_profile" "$HOME/.bash_login"; do
        if [[ -f "$login_profile" ]]; then profiles+=("$login_profile"); fi
    done
    for profile in "${profiles[@]}"; do
        if ! grep -Fqx -- "$path_line" "$profile" 2>/dev/null; then
            printf '\n# Oryn user installation\n%s\n' "$path_line" >> "$profile"
        fi
    done
fi
printf '\nInstalled %s (%s).\n' "$launcher" "$runtime"
printf 'For this terminal, run:\n  %s\nThen launch: oryn\n' "$path_line"
if [[ "$update_path" == true ]]; then
    printf 'Bash/Zsh PATH configured for new terminals.\n'
fi
