#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

SERVICE_USER="${DOTMATTER_SERVICE_USER:-dotmatter}"
SERVICE_GROUP="${DOTMATTER_SERVICE_GROUP:-${SERVICE_USER}}"
DEBUG_PATH="${DOTMATTER_DEBUG_PATH:-/opt/dot-matter}"
AOT_PATH="${DOTMATTER_AOT_PATH:-/opt/dot-matter-aot}"
STATE_PATH="${DOTMATTER_STATE_PATH:-/var/lib/.dot-matter}"
ENV_DIR="${DOTMATTER_ENV_DIR:-/etc/dotmatter}"

SYSTEMD_DIR="${DOTMATTER_SYSTEMD_DIR:-/etc/systemd/system}"
SAMBA_ROOT="${DOTMATTER_SAMBA_ROOT:-/etc/samba}"
SAMBA_MAIN_CONF="${DOTMATTER_SAMBA_MAIN_CONF:-${SAMBA_ROOT}/smb.conf}"
SAMBA_INCLUDE_CONF="${DOTMATTER_SAMBA_INCLUDE_CONF:-${SAMBA_ROOT}/dotmatter-shares.conf}"
OTBR_SUDOERS_FILE="${DOTMATTER_OTBR_SUDOERS_FILE:-/etc/sudoers.d/dotmatter-otbr}"

DEBUG_SERVICE_NAME="dot-matter.service"
AOT_SERVICE_NAME="dot-matter-aot.service"
DEBUG_SERVICE_SOURCE="${REPO_ROOT}/${DEBUG_SERVICE_NAME}"
AOT_SERVICE_SOURCE="${REPO_ROOT}/${AOT_SERVICE_NAME}"
DEBUG_SERVICE_TARGET="${SYSTEMD_DIR}/${DEBUG_SERVICE_NAME}"
AOT_SERVICE_TARGET="${SYSTEMD_DIR}/${AOT_SERVICE_NAME}"

DEBUG_ENV_EXAMPLE_SOURCE="${REPO_ROOT}/dot-matter.env.example"
AOT_ENV_EXAMPLE_SOURCE="${REPO_ROOT}/dot-matter-aot.env.example"
DEBUG_ENV_TARGET="${ENV_DIR}/dot-matter.env"
AOT_ENV_TARGET="${ENV_DIR}/dot-matter-aot.env"

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    echo "This script must run as root." >&2
    exit 1
  fi
}

require_file() {
  local file_path="$1"
  if [[ ! -f "${file_path}" ]]; then
    echo "Required file not found: ${file_path}" >&2
    exit 1
  fi
}

create_service_account() {
  if ! id -u "${SERVICE_USER}" >/dev/null 2>&1; then
    useradd --system --home "${STATE_PATH}" --shell /usr/sbin/nologin "${SERVICE_USER}"
  fi
}

prepare_directories() {
  mkdir -p "${DEBUG_PATH}" "${AOT_PATH}" "${STATE_PATH}" "${ENV_DIR}" "${SYSTEMD_DIR}" "${SAMBA_ROOT}"
  chown "${SERVICE_USER}:${SERVICE_GROUP}" "${DEBUG_PATH}" "${AOT_PATH}" "${STATE_PATH}"
  chmod 750 "${ENV_DIR}"
}

install_service_file() {
  local source_path="$1"
  local target_path="$2"
  rm -f "${target_path}"
  install -m 0644 "${source_path}" "${target_path}"
}

ensure_env_file() {
  local source_path="$1"
  local target_path="$2"
  if [[ ! -f "${target_path}" ]]; then
    install -m 0640 "${source_path}" "${target_path}"
  fi
  chown root:"${SERVICE_GROUP}" "${target_path}"
  chmod 0640 "${target_path}"
}

write_samba_share_file() {
  rm -f "${SAMBA_INCLUDE_CONF}"
  cat > "${SAMBA_INCLUDE_CONF}" <<EOF
[dot-matter]
   path = ${DEBUG_PATH}
   browseable = yes
   read only = no
   valid users = ${SERVICE_USER}
   create mask = 0755
   directory mask = 0755

[dot-matter-aot]
   path = ${AOT_PATH}
   browseable = yes
   read only = no
   valid users = ${SERVICE_USER}
   create mask = 0755
   directory mask = 0755
EOF
  chmod 0644 "${SAMBA_INCLUDE_CONF}"
}

ensure_samba_include() {
  local include_line="include = ${SAMBA_INCLUDE_CONF}"
  if [[ ! -f "${SAMBA_MAIN_CONF}" ]]; then
    echo "Samba main config not found: ${SAMBA_MAIN_CONF}" >&2
    exit 1
  fi

  if ! grep -Fqx "${include_line}" "${SAMBA_MAIN_CONF}"; then
    printf '\n# DotMatter shares\n%s\n' "${include_line}" >> "${SAMBA_MAIN_CONF}"
  fi
}

reload_services() {
  systemctl daemon-reload
  systemctl enable dot-matter dot-matter-aot

  if command -v systemd-analyze >/dev/null 2>&1; then
    systemd-analyze verify "${DEBUG_SERVICE_TARGET}" "${AOT_SERVICE_TARGET}"
  fi
}

reload_samba() {
  if command -v testparm >/dev/null 2>&1; then
    testparm -s >/dev/null
  fi

  if systemctl is-enabled smbd >/dev/null 2>&1 || systemctl is-active smbd >/dev/null 2>&1; then
    systemctl restart smbd
  fi
}

install_otbr_sudoers() {
  cat > "${OTBR_SUDOERS_FILE}" <<EOF
${SERVICE_USER} ALL=(ALL) NOPASSWD: /usr/sbin/ot-ctl
EOF
  chmod 0440 "${OTBR_SUDOERS_FILE}"

  if command -v visudo >/dev/null 2>&1; then
    visudo -cf "${OTBR_SUDOERS_FILE}"
  fi
}

main() {
  require_root

  require_file "${DEBUG_SERVICE_SOURCE}"
  require_file "${AOT_SERVICE_SOURCE}"
  require_file "${DEBUG_ENV_EXAMPLE_SOURCE}"
  require_file "${AOT_ENV_EXAMPLE_SOURCE}"

  create_service_account
  prepare_directories

  install_service_file "${DEBUG_SERVICE_SOURCE}" "${DEBUG_SERVICE_TARGET}"
  install_service_file "${AOT_SERVICE_SOURCE}" "${AOT_SERVICE_TARGET}"

  ensure_env_file "${DEBUG_ENV_EXAMPLE_SOURCE}" "${DEBUG_ENV_TARGET}"
  ensure_env_file "${AOT_ENV_EXAMPLE_SOURCE}" "${AOT_ENV_TARGET}"
  install_otbr_sudoers

  write_samba_share_file
  ensure_samba_include

  reload_services
  reload_samba

  cat <<EOF
Installed:
  ${DEBUG_SERVICE_TARGET}
  ${AOT_SERVICE_TARGET}
  ${SAMBA_INCLUDE_CONF}
  ${OTBR_SUDOERS_FILE}

Created if missing:
  ${DEBUG_ENV_TARGET}
  ${AOT_ENV_TARGET}

Next steps:
  1. Edit ${DEBUG_ENV_TARGET} and ${AOT_ENV_TARGET} for host-local runtime values.
  2. Set a Samba password for ${SERVICE_USER} with: smbpasswd -a ${SERVICE_USER}
  3. Deploy with your local MSBuild props workflow.
EOF
}

main "$@"
