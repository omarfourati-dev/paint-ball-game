#!/usr/bin/env bash
# Legt die geprüften Desktop-Dateien aus dem Pipeline-Artefakt nach downloads/ (Self-hosted-Deploy, Spec §3).
# Aufruf: publish-downloads.sh <Artefakt-Ordner> <Ziel-Ordner>
# Erst alles prüfen (Prüfsummen, Dateinamen, latest.json nennt nur geprüfte Dateien), dann jede Datei als .tmp im Ziel
# ablegen, dort erneut prüfen und per mv (atomar, gleiches Dateisystem) einsetzen: .exe → SHA256SUMS.txt → latest.json
# (zuletzt). Danach alte Versionen weg. Bricht es vorher ab, bleibt die alte Version vollständig und gültig stehen.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Aufruf: publish-downloads.sh <Artefakt-Ordner> <Ziel-Ordner>" >&2
  exit 2
fi
src=$1
dest=$2

if [ ! -f "$src/SHA256SUMS.txt" ] || [ ! -f "$src/latest.json" ]; then
  echo "Artefakt unvollständig" >&2
  exit 1
fi
(cd "$src" && sha256sum --strict -c SHA256SUMS.txt)

names=()
hashes=()
# "|| [ -n ... ]": auch eine letzte Zeile ohne Zeilenumbruch zählt
while read -r hash name || [ -n "${name:-}" ]; do
  [[ "$name" =~ ^PaintBall-[A-Za-z0-9.-]+\.exe$ ]] || { echo "Unerwarteter Dateiname: $name" >&2; exit 1; }
  names+=("$name")
  hashes+=("$hash")
done < "$src/SHA256SUMS.txt"
[ "${#names[@]}" -gt 0 ] || { echo "SHA256SUMS.txt ist leer" >&2; exit 1; }

# latest.json darf nur Dateien nennen, die geprüft und gleich mit abgelegt werden (sonst zeigt die Landingpage ins Leere)
listed=$(grep -o '"file"[[:space:]]*:[[:space:]]*"[^"]*"' "$src/latest.json" | sed 's/.*"\([^"]*\)"$/\1/' || true)
[ -n "$listed" ] || { echo "latest.json nennt keine Datei" >&2; exit 1; }
while read -r file; do
  found=0
  for name in "${names[@]}"; do [ "$file" = "$name" ] && found=1; done
  [ "$found" = 1 ] || { echo "latest.json nennt ungeprüfte Datei: $file" >&2; exit 1; }
done <<< "$listed"

mkdir -p "$dest"
tmps=()
cleanup() { [ "${#tmps[@]}" -eq 0 ] || rm -f "${tmps[@]}"; }
trap cleanup EXIT

stage() { # Datei als verstecktes .tmp ins Ziel kopieren
  cp -f "$src/$1" "$dest/.$1.tmp"
  chmod 644 "$dest/.$1.tmp"
  tmps+=("$dest/.$1.tmp")
}
for i in "${!names[@]}"; do
  stage "${names[$i]}"
  actual=$(sha256sum "$dest/.${names[$i]}.tmp" | cut -d' ' -f1)
  [ "$actual" = "${hashes[$i]}" ] || { echo "Kopie fehlerhaft: ${names[$i]}" >&2; exit 1; }
done
stage SHA256SUMS.txt
stage latest.json

for name in "${names[@]}"; do mv -f "$dest/.$name.tmp" "$dest/$name"; done
mv -f "$dest/.SHA256SUMS.txt.tmp" "$dest/SHA256SUMS.txt"
mv -f "$dest/.latest.json.tmp" "$dest/latest.json"
tmps=()

shopt -s nullglob
for old in "$dest"/PaintBall-*.exe; do
  keep=0
  for name in "${names[@]}"; do [ "$(basename "$old")" = "$name" ] && keep=1; done
  [ "$keep" = 1 ] || rm -f "$old"
done
echo "Desktop-Downloads aktualisiert: ${names[*]}"
