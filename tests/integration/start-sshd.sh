#!/usr/bin/env bash
# Démarre un serveur OpenSSH jetable pour les tests d'intégration de Quai.Core (Linux, root ou sudo).
# Écrit les variables QUAI_TEST_SSH_* dans le fichier passé en argument (ou sur la sortie standard).
set -euo pipefail

PORT="${QUAI_TEST_SSH_PORT:-2222}"
WORK="${QUAI_TEST_SSH_DIR:-$(mktemp -d)}"
USER_NAME="quaitest"
SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

command -v sshd >/dev/null || { $SUDO apt-get update -qq && $SUDO apt-get install -y -qq openssh-server; }
SSHD="$(command -v sshd || echo /usr/sbin/sshd)"

# Utilisateur de test avec un mot de passe aléatoire (jamais stocké dans le dépôt).
PASSWORD="$(head -c 18 /dev/urandom | base64 | tr -d '/+=' | head -c 20)"
if ! id "$USER_NAME" >/dev/null 2>&1; then
  $SUDO useradd --create-home --shell /bin/bash "$USER_NAME"
fi
echo "$USER_NAME:$PASSWORD" | $SUDO chpasswd

mkdir -p "$WORK"
chmod 755 "$WORK"

# Clés d'hôte : deux jeux, pour simuler un changement d'empreinte.
ssh-keygen -q -t ed25519 -N "" -f "$WORK/host_ed25519"
ssh-keygen -q -t ed25519 -N "" -f "$WORK/host_ed25519_alt"

# Clés client : une sans passphrase, une chiffrée.
PASSPHRASE="$(head -c 18 /dev/urandom | base64 | tr -d '/+=' | head -c 16)"
ssh-keygen -q -t ed25519 -N "" -f "$WORK/client_plain" -C "quai-test-plain"
ssh-keygen -q -t ed25519 -N "$PASSPHRASE" -f "$WORK/client_encrypted" -C "quai-test-encrypted"
ssh-keygen -q -t rsa -b 3072 -N "" -f "$WORK/client_rsa" -C "quai-test-rsa"
chmod 600 "$WORK"/client_plain "$WORK"/client_encrypted "$WORK"/client_rsa
HOME_DIR="$(getent passwd "$USER_NAME" | cut -d: -f6)"
$SUDO install -d -m 700 -o "$USER_NAME" -g "$USER_NAME" "$HOME_DIR/.ssh"
cat "$WORK/client_plain.pub" "$WORK/client_encrypted.pub" "$WORK/client_rsa.pub" | $SUDO tee "$HOME_DIR/.ssh/authorized_keys" >/dev/null
$SUDO chown "$USER_NAME:$USER_NAME" "$HOME_DIR/.ssh/authorized_keys"
$SUDO chmod 600 "$HOME_DIR/.ssh/authorized_keys"

write_config() { # $1 = fichier, $2 = port, $3 = clé d'hôte
  cat > "$1" <<CONF
Port $2
ListenAddress 127.0.0.1
HostKey $3
PidFile $WORK/$(basename "$1").pid
PasswordAuthentication yes
KbdInteractiveAuthentication no
PubkeyAuthentication yes
UsePAM no
PermitRootLogin no
AllowUsers $USER_NAME
Subsystem sftp internal-sftp
LogLevel ERROR
CONF
}

# Deux serveurs : le second présente une autre clé d'hôte (même machine, autre port).
ALT_PORT=$((PORT + 1))
write_config "$WORK/sshd_config" "$PORT" "$WORK/host_ed25519"
write_config "$WORK/sshd_config_alt" "$ALT_PORT" "$WORK/host_ed25519_alt"
$SUDO mkdir -p /run/sshd
$SUDO "$SSHD" -f "$WORK/sshd_config"
$SUDO "$SSHD" -f "$WORK/sshd_config_alt"

OUT="${1:-/dev/stdout}"
{
  echo "QUAI_TEST_SSH_HOST=127.0.0.1"
  echo "QUAI_TEST_SSH_PORT=$PORT"
  echo "QUAI_TEST_SSH_ALT_PORT=$ALT_PORT"
  echo "QUAI_TEST_SSH_USER=$USER_NAME"
  echo "QUAI_TEST_SSH_PASSWORD=$PASSWORD"
  echo "QUAI_TEST_SSH_KEY=$WORK/client_plain"
  echo "QUAI_TEST_SSH_KEY_RSA=$WORK/client_rsa"
  echo "QUAI_TEST_SSH_KEY_ENCRYPTED=$WORK/client_encrypted"
  echo "QUAI_TEST_SSH_KEY_PASSPHRASE=$PASSPHRASE"
} >> "$OUT"
