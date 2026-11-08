"""Build a Jellyfin installation ZIP from compiled plugin assemblies."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src/Jellyfin.Plugin.SidecarBridge"
ASSEMBLIES = ["SidecarBridge.Core.dll", "Jellyfin.Plugin.SidecarBridge.dll"]


def metadata():
    project = ET.parse(PROJECT / "Jellyfin.Plugin.SidecarBridge.csproj")
    version = project.findtext("./PropertyGroup/Version")
    if not version or not re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", version):
        raise ValueError("Use a four-part numeric plugin version in the project")
    controller = project.find("./ItemGroup/PackageReference[@Include='Jellyfin.Controller']")
    dependency = controller.attrib["Version"]
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){2,3}", dependency):
        raise ValueError("Pin Jellyfin.Controller to the tested stable version")
    abi = ".".join((dependency.split(".") + ["0"])[:4])

    sections = re.split(r"(?m)^## (\S+)\s*$", (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"))
    notes = dict(zip(sections[1::2], sections[2::2]))[version].strip()
    if not notes:
        raise ValueError(f"Missing release notes for {version}")
    [plugin] = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    fields = ("guid", "name", "owner", "category", "overview", "description")
    return {
        **{key: plugin[key] for key in fields},
        "version": version,
        "targetAbi": abi,
        "changelog": notes,
        "status": "Active",
        "autoUpdate": True,
        "imagePath": "icon.png",
        "assemblies": ASSEMBLIES,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, default=PROJECT / "bin/Release/net10.0")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    meta = metadata()
    payload = {name: (args.build_dir / name).read_bytes() for name in ASSEMBLIES}
    payload["meta.json"] = (json.dumps(meta, ensure_ascii=False, indent=2) + "\n").encode()
    payload["icon.png"] = (ROOT / "docs/assets/icon.png").read_bytes()
    payload["LICENSE"] = (ROOT / "LICENSE").read_bytes()
    payload["INSTALL.txt"] = (
        "Add the repository URL from the project README in Jellyfin's Plugins settings.\n"
        "Install Sidecar Bridge from the catalog, restart Jellyfin and enable your libraries.\n"
    ).encode()

    args.output.mkdir(parents=True, exist_ok=True)
    target = args.output / f"SidecarBridge_{meta['version']}.zip"
    with zipfile.ZipFile(target, "w") as archive:
        for name, data in sorted(payload.items()):
            item = zipfile.ZipInfo(name)
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

    digest = hashlib.sha256(target.read_bytes()).hexdigest()
    target.with_suffix(".zip.sha256").write_text(f"{digest}  {target.name}\n", encoding="utf-8")
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as output:
            print(f"zip={target.relative_to(ROOT).as_posix()}", file=output)
    print(f"{target}\nSHA256 {digest}")


if __name__ == "__main__":
    main()
