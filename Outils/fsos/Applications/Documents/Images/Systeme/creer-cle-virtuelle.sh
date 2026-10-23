#!/usr/bin/env bash
# =====================================================================
#  File System OS - File System OS Company - Vincent Senut
#  "Cle virtuelle" : met un dossier de fichiers (images, textes, .fsl)
#  sur un petit disque FAT32 a brancher sur la machine virtuelle.
#  Dans File System OS, il apparait dans l'explorateur (ex : 1:\).
#
#    ./creer-cle-virtuelle.sh mes_fichiers/            -> cle.img / .vdi / .vmdk
#    ./creer-cle-virtuelle.sh mes_fichiers/ --taille 512
#
#  Les images .png / .jpg sont converties en .bmp 24 bits (si Python
#  Pillow est installe : pip install pillow), car l'OS lit le BMP.
# =====================================================================
set -euo pipefail

SRC=""; SIZE_MB=256; OUT="cle"
while [ $# -gt 0 ]; do
    case "$1" in
        --taille) SIZE_MB="$2"; shift 2 ;;
        --nom)    OUT="$2";     shift 2 ;;
        *) SRC="$1"; shift ;;
    esac
done
[ -n "$SRC" ] && [ -d "$SRC" ] || { echo "Donnez le dossier a copier sur la cle."; exit 1; }
for t in sfdisk mkfs.fat mcopy python3; do
    command -v "$t" >/dev/null || { echo "Outil manquant : $t (sudo apt install fdisk dosfstools mtools python3)"; exit 1; }
done

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/files"
cp -r "$SRC"/. "$WORK/files/"

echo "[1/3] Conversion des images en BMP 24 bits"
python3 - "$WORK/files" <<'PY'
import os, sys
try:
    from PIL import Image
except ImportError:
    print("      (Pillow absent : les .png/.jpg sont copies tels quels)"); sys.exit(0)
n = 0
for root, _, files in os.walk(sys.argv[1]):
    for f in files:
        if f.lower().endswith(('.png', '.jpg', '.jpeg', '.gif', '.webp')):
            p = os.path.join(root, f)
            im = Image.open(p).convert('RGB')
            if max(im.size) > 1920: im.thumbnail((1920, 1920))
            im.save(os.path.splitext(p)[0] + '.bmp'); os.remove(p); n += 1
print(f"      {n} image(s) convertie(s)")
PY

echo "[2/3] Disque FAT32 de $SIZE_MB Mo"
PART="$WORK/part.img"
truncate -s "$((SIZE_MB - 1))M" "$PART"
mkfs.fat -F 32 -n CLE -s 8 -h 2048 "$PART" >/dev/null
export MTOOLS_SKIP_CHECK=1
mcopy -i "$PART" -s "$WORK/files"/* ::/
rm -f "$OUT.img"; truncate -s "${SIZE_MB}M" "$OUT.img"
echo 'start=2048, type=c' | sfdisk --quiet "$OUT.img"
dd if="$PART" of="$OUT.img" bs=1M seek=1 conv=notrunc status=none

echo "[3/3] Formats machines virtuelles"
if command -v qemu-img >/dev/null; then
    qemu-img convert -f raw -O vdi  "$OUT.img" "$OUT.vdi"  && echo "      $OUT.vdi  (VirtualBox)"
    qemu-img convert -f raw -O vmdk "$OUT.img" "$OUT.vmdk" && echo "      $OUT.vmdk (VMware)"
fi
echo "Termine : branchez $OUT.vdi comme 2e disque IDE de la VM, puis ouvrez l'explorateur."
