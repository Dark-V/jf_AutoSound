"""Publish the ZIP tested by Actions and append its entry to the Jellyfin catalog."""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
from urllib.error import URLError
from urllib.request import Request, urlopen
import zipfile

from package import metadata


def gh(*arguments):
    return subprocess.check_output(["gh", *arguments], text=True, encoding="utf-8")


def release_info(repo, tag):
    result = subprocess.run(
        ["gh", "release", "view", tag, "--repo", repo, "--json", "isDraft,isPrerelease,assets"],
        capture_output=True, text=True, encoding="utf-8", check=False,
    )
    if result.returncode == 0:
        return json.loads(result.stdout)
    if result.stderr.strip() == "release not found":
        return None
    raise RuntimeError(result.stderr.strip() or "Unable to query the GitHub release")


def download(url):
    request = Request(url, headers={"User-Agent": "SidecarBridge release verification", "Cache-Control": "no-cache"})
    with urlopen(request, timeout=20) as response:
        return response.read()


def wait_for_public_file(url, matches, seconds):
    deadline = time.monotonic() + seconds
    while True:
        try:
            data = download(url)
            if matches(data):
                return data
        except (URLError, TimeoutError):
            pass
        if time.monotonic() >= deadline:
            raise RuntimeError(f"The expected contents were not available at {url}")
        time.sleep(5)


def publish(repo, tag, meta, files):
    release = release_info(repo, tag)
    if release is None:
        gh("release", "create", tag, "--repo", repo, "--verify-tag", "--draft",
           "--title", f"Sidecar Bridge {meta['version']}", "--notes", meta["changelog"])
        release = release_info(repo, tag)
    if release["isPrerelease"]:
        raise ValueError(f"{tag} is already a prerelease")
    if not release["isDraft"]:
        return

    uploaded = {asset["name"] for asset in release["assets"]}
    missing = [str(file) for file in files if file.name not in uploaded]
    if missing:
        gh("release", "upload", tag, *missing, "--repo", repo)
    with tempfile.TemporaryDirectory() as directory:
        for file in files:
            gh("release", "download", tag, "--repo", repo, "--pattern", file.name, "--dir", directory)
            if (Path(directory) / file.name).read_bytes() != file.read_bytes():
                raise ValueError(f"Existing draft asset differs from the tested artifact: {file.name}")
    gh("release", "edit", tag, "--repo", repo, "--draft=false", "--latest")


def catalog_package(catalog, guid):
    [package] = [item for item in catalog if item["guid"].lower() == guid.lower()]
    return package


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("publish", "verify"))
    parser.add_argument("--zip", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    repo = os.environ["GITHUB_REPOSITORY"]
    tag = os.environ["GITHUB_REF_NAME"]
    with zipfile.ZipFile(args.zip) as archive:
        meta = json.loads(archive.read("meta.json"))
        if archive.testzip() is not None or meta != metadata():
            raise ValueError("The tested ZIP does not match the tagged source metadata")
    version = meta["version"]
    if tag != f"v{version}" or args.zip.name != f"SidecarBridge_{version}.zip":
        raise ValueError("Release tag, package version and filename must agree")

    catalog = json.loads(args.manifest.read_text(encoding="utf-8"))
    package = catalog_package(catalog, meta["guid"])
    versions = package["versions"]
    previous = [entry for entry in versions if entry["version"] == version]
    if len(previous) > 1:
        raise ValueError(f"Catalog already contains duplicate entries for {version}")

    zip_bytes = args.zip.read_bytes()
    sha_file = args.zip.with_suffix(".zip.sha256")
    sha_bytes = f"{hashlib.sha256(zip_bytes).hexdigest()}  {args.zip.name}\n".encode()
    if sha_file.read_bytes() != sha_bytes:
        raise ValueError("The checksum file does not match the tested ZIP")
    if args.phase == "publish":
        publish(repo, tag, meta, [args.zip, sha_file])

    release = json.loads(gh("api", f"repos/{repo}/releases/tags/{tag}"))
    if release["draft"] or release["prerelease"] or not release["published_at"]:
        raise ValueError("A public stable release is required")
    timestamp = release["published_at"]
    datetime.fromisoformat(timestamp)
    assets = {asset["name"]: asset for asset in release["assets"] if asset["state"] == "uploaded"}
    for name, expected in [(args.zip.name, zip_bytes), (sha_file.name, sha_bytes)]:
        url = assets[name]["browser_download_url"]
        if url != f"https://github.com/{repo}/releases/download/{tag}/{name}":
            raise ValueError(f"Unexpected public asset URL for {name}")
        wait_for_public_file(url, lambda data: data == expected, 90)
    entry = {
        "version": version,
        "targetAbi": meta["targetAbi"],
        "sourceUrl": assets[args.zip.name]["browser_download_url"],
        "checksum": hashlib.md5(zip_bytes, usedforsecurity=False).hexdigest(),
        "timestamp": timestamp,
        "changelog": meta["changelog"],
    }
    if previous and previous[0] != entry:
        raise ValueError(f"Refusing to replace published catalog entry {version}")

    if args.phase == "publish" and not previous:
        versions.append(entry)
        versions.sort(key=lambda item: tuple(map(int, item["version"].split("."))), reverse=True)
        args.manifest.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.phase == "verify":
        raw_url = f"https://raw.githubusercontent.com/{repo}/refs/heads/main/manifest.json"

        def contains_release(data):
            return entry in catalog_package(json.loads(data), meta["guid"])["versions"]

        wait_for_public_file(raw_url, contains_release, 360)
        print(f"Verified public catalog: {raw_url}")
    print(f"{release['html_url']}\n{entry['sourceUrl']}\nMD5 {entry['checksum']}")


if __name__ == "__main__":
    main()
