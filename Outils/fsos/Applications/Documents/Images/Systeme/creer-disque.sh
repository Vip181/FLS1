#!/usr/bin/env bash
# =====================================================================
#  File System OS - File System OS Company
#  Cree un DISQUE DEMARRABLE : une fois installe, plus besoin de l'ISO
#  ni de la cle USB.
#
#  Utilisation (Linux ou WSL sous Windows) :
#    ./creer-disque.sh FileSystemOS.iso                 -> image + VDI + VMDK
#    ./creer-disque.sh FileSystemOS.iso --taille 2048   -> disque de 2 Go
#    sudo ./creer-disque.sh FileSystemOS.iso --vers /dev/sdX
#    sudo ./creer-disque.sh FileSystemOS.iso --vers /dev/nvme0n1
#                     -> INSTALLE sur un disque interne, SATA ou NVMe (l'efface !)
#
#  Contenu du disque cree (MBR + 1 partition FAT32 "FSOS") :
#    /boot/FileSystemOS.bin          le noyau (extrait de l'ISO)
#    /boot/limine/limine-bios.sys    chargeur de demarrage Limine
#    /boot/limine/limine.conf
#    /FSOS/...                       environnement deja installe
# =====================================================================
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
SOURCE=""; SIZE_MB=1024; TARGET=""; OUT="FileSystemOS-disque"

while [ $# -gt 0 ]; do
    case "$1" in
        --taille) SIZE_MB="$2"; shift 2 ;;
        --vers)   TARGET="$2";  shift 2 ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) SOURCE="$1"; shift ;;
    esac
done

[ -n "$SOURCE" ] && [ -f "$SOURCE" ] || { echo "Donnez l'ISO (ou le noyau .bin) de File System OS."; exit 1; }
[ "$SIZE_MB" -ge 64 ] || { echo "Taille minimale : 64 Mo"; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || { echo "Outil manquant : $1  ->  $2"; exit 1; }; }
need sfdisk   "sudo apt install fdisk"
need mkfs.fat "sudo apt install dosfstools"
need mcopy    "sudo apt install mtools"
need git      "sudo apt install git"
need make     "sudo apt install build-essential"
need python3  "sudo apt install python3"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ---------------------------------------------------------------------
echo "[1/6] Recherche du noyau"
case "${SOURCE,,}" in
    *.iso)
        mkdir -p "$WORK/iso"
        if command -v 7z >/dev/null; then 7z x -y -o"$WORK/iso" "$SOURCE" >/dev/null
        elif command -v osirrox >/dev/null; then osirrox -indev "$SOURCE" -extract / "$WORK/iso" >/dev/null 2>&1
        else echo "Outil manquant : 7z  ->  sudo apt install p7zip-full"; exit 1; fi
        KERNEL="$(find "$WORK/iso" -type f \( -iname '*.bin' -o -iname '*.elf' \) -size +100k \
                  -printf '%s %p\n' | sort -n | tail -1 | cut -d' ' -f2-)"
        ;;
    *) KERNEL="$SOURCE" ;;
esac
[ -n "${KERNEL:-}" ] && [ -f "$KERNEL" ] || { echo "Noyau introuvable dans l'ISO."; exit 1; }
echo "      noyau : $(basename "$KERNEL") ($(du -h "$KERNEL" | cut -f1))"

# Multiboot 1 ou 2 ? (on cherche la signature dans les 32 premiers Ko)
PROTO="$(python3 - "$KERNEL" <<'PY'
import struct, sys
d = open(sys.argv[1], 'rb').read(32768)
for off in range(0, len(d) - 4, 4):
    m = struct.unpack_from('<I', d, off)[0]
    if m == 0xE85250D6 and off % 8 == 0: print('multiboot2'); break
    if m == 0x1BADB002: print('multiboot1'); break
else:
    print('multiboot1')
PY
)"
echo "      protocole : $PROTO"

# ---------------------------------------------------------------------
echo "[2/6] Chargeur de demarrage Limine"
LIMINE="$DIR/limine"
if [ ! -f "$LIMINE/limine-bios.sys" ]; then
    git clone --quiet --branch=v8.x-binary --depth=1 https://github.com/limine-bootloader/limine.git "$LIMINE"
fi
[ -x "$LIMINE/limine" ] || make -s -C "$LIMINE"

cat > "$WORK/limine.conf" <<CONF
timeout: 3

/File System OS
    comment: File System OS Company
    protocol: $PROTO
    path: boot():/boot/FileSystemOS.bin
    resolution: 1024x768x32
CONF

# ---------------------------------------------------------------------
echo "[3/6] Partition FAT32 de $((SIZE_MB - 1)) Mo"
PART="$WORK/part.img"
truncate -s "$((SIZE_MB - 1))M" "$PART"
# FAT32 standard (clusters de 4 Ko), compatible avec le pilote FAT de Cosmos
mkfs.fat -F 32 -n FSOS -s 8 -h 2048 "$PART" >/dev/null

export MTOOLS_SKIP_CHECK=1
mmd   -i "$PART" ::/boot ::/boot/limine ::/FSOS
mcopy -i "$PART" "$KERNEL"                  ::/boot/FileSystemOS.bin
mcopy -i "$PART" "$LIMINE/limine-bios.sys"  ::/boot/limine/
mcopy -i "$PART" "$WORK/limine.conf"        ::/boot/limine/
echo "[4/6] Installation de l'environnement \\FSOS"
mcopy -i "$PART" -s "$DIR/fsos/Systeme" "$DIR/fsos/Applications" "$DIR/fsos/Documents" "$DIR/fsos/Images" ::/FSOS/

# ---------------------------------------------------------------------
echo "[5/6] Disque complet (MBR + partition + Limine)"
IMG="$OUT.img"
rm -f "$IMG"
truncate -s "${SIZE_MB}M" "$IMG"
echo 'start=2048, type=c, bootable' | sfdisk --quiet "$IMG"
dd if="$PART" of="$IMG" bs=1M seek=1 conv=notrunc status=none
"$LIMINE/limine" bios-install "$IMG" >/dev/null
echo "      $IMG pret"

# ---------------------------------------------------------------------
echo "[6/6] Formats pour machines virtuelles"
if command -v qemu-img >/dev/null; then
    qemu-img convert -f raw -O vdi  "$IMG" "$OUT.vdi"  && echo "      $OUT.vdi   (VirtualBox)"
    qemu-img convert -f raw -O vmdk "$IMG" "$OUT.vmdk" && echo "      $OUT.vmdk  (VMware)"
elif command -v VBoxManage >/dev/null; then
    VBoxManage convertfromraw "$IMG" "$OUT.vdi" --format VDI >/dev/null && echo "      $OUT.vdi   (VirtualBox)"
else
    echo "      (installez qemu-utils pour obtenir .vdi et .vmdk : sudo apt install qemu-utils)"
fi

# ---------------------------------------------------------------------
if [ -n "$TARGET" ]; then
    [ -b "$TARGET" ] || { echo "$TARGET n'est pas un disque."; exit 1; }
    case "$TARGET" in
        *nvme*) echo "Disque NVMe detecte : File System OS 1.4 a un pilote NVMe." ;;
    esac
    if mount | grep -q "^$TARGET"; then
        echo "ATTENTION : des partitions de $TARGET sont montees. Demontez-les d'abord :"
        mount | grep "^$TARGET" | awk '{print "   sudo umount " $1}'
        exit 1
    fi
    echo
    lsblk -o NAME,SIZE,MODEL "$TARGET" || true
    echo
    echo "ATTENTION : TOUT le contenu de $TARGET va etre EFFACE."
    read -r -p "Tapez OUI en majuscules pour installer File System OS : " ok
    [ "$ok" = "OUI" ] || { echo "Annule."; exit 1; }
    dd if="$IMG" of="$TARGET" bs=4M conv=fsync status=progress
    sync
    echo "Installe sur $TARGET. Retirez la cle USB et redemarrez (mode BIOS / Legacy)."
fi

echo "Termine."
