"""Read-only release audit. Exit 2 means the source/artifact is not release-ready.

Run with the final APK: python tools/release_preflight.py --apk <file> --aapt <aapt.exe>
Reports never imply native tests, signatures, teaching review or store approval passed.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import subprocess
import wave
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ANDROID = "{http://schemas.android.com/apk/res/android}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path)
    parser.add_argument("--aapt", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    checks = []

    def record(name, passed, detail):
        checks.append({"check": name, "status": "PASS" if passed else "BLOCKED", "detail": detail})

    project = ET.parse(ROOT / "HanMate.App/HanMate.App.csproj").getroot()
    identity = project.findtext(".//ApplicationId", "")
    record("release_identity", bool(identity) and "companyname" not in identity,
           "ApplicationId=" + identity + "; installed developer identity must not be silently changed.")
    windows = ET.parse(ROOT / "HanMate.App/Platforms/Windows/Package.appxmanifest").getroot()
    package_id = windows.find("{*}Identity")
    record("windows_publisher", package_id is not None and "placeholder" not in package_id.get("Name", "") and package_id.get("Publisher") != "CN=User Name",
           "Store identity and signing publisher require the actual release owner.")

    source_manifest = ET.parse(ROOT / "HanMate.App/Platforms/Android/AndroidManifest.xml").getroot()
    permissions = sorted(x.get(ANDROID + "name", "") for x in source_manifest.findall("uses-permission"))
    record("source_android_permissions", permissions == ["android.permission.INTERNET", "android.permission.RECORD_AUDIO"], permissions)
    app = source_manifest.find("application")
    record("android_auto_backup", app is not None and app.get(ANDROID + "allowBackup") == "false"
           and app.get(ANDROID + "dataExtractionRules") == "@xml/data_extraction_rules",
           "Auto-backup is disabled; explicit app backup remains available.")
    paths = ET.parse(ROOT / "HanMate.App/Platforms/Android/Resources/xml/microsoft_maui_essentials_fileprovider_file_paths.xml").getroot()
    record("source_share_scope", len(paths) == 1 and paths[0].tag == "cache-path" and paths[0].get("path") == "sharing/",
           "Only private cache/sharing is exposed to the file provider.")

    dependencies = []
    for name in ["HanMate.Core", "HanMate.Infrastructure", "HanMate.App", "HanMate.Core.Tests", "HanMate.Infrastructure.Tests"]:
        lock = ROOT / name / "packages.lock.json"
        record("lock_" + name, lock.is_file(), "NuGet content hashes are recorded; restore --locked-mode verifies them.")
        if lock.is_file():
            parsed = json.loads(lock.read_text(encoding="utf-8-sig"))
            dependencies.append({"project": name, "sha256": hashlib.sha256(lock.read_bytes()).hexdigest(),
                                 "frameworks": parsed.get("dependencies", {})})
    resx = []
    for suffix in ["", ".zh-Hans", ".ja"]:
        values = {n.get("name"): n.findtext("value", "") for n in ET.parse(ROOT / ("HanMate.App/Localization/AppResources" + suffix + ".resx")).getroot().findall("data")}
        resx.append(values)
    valid = all(set(d) == set(resx[0]) and all(d.values()) for d in resx)
    valid = valid and all(sorted(re.findall(r"\{\d+(?:[^{}]*)\}", d[k])) == sorted(re.findall(r"\{\d+(?:[^{}]*)\}", resx[0][k])) for d in resx for k in resx[0] if k in d)
    record("localization", valid, {"keys": len(resx[0]), "languages": 3})

    with (ROOT / "HanMate_Planning_Pack_v3.0/tracking/backlog.csv").open(encoding="utf-8-sig", newline="") as file:
        rows = list(csv.DictReader(file))
    # Keep the work-package evidence as a gate, without replacing the 176 application cases.
    remaining = [row for row in rows if row.get("status") != "DONE"]
    record("work_packages", not remaining, remaining)
    # Internal delivery completion must never silently waive deferred public-release gates.
    gate_path = ROOT / 'HanMate_Planning_Pack_v3.0/tracking/deferred-release-gates.json'
    if gate_path.exists():
        deferred = json.loads(gate_path.read_text(encoding='utf-8'))
        gates = deferred.get('gates', [])
        ready = bool(gates) and all(g.get('status') == 'PASS' and g.get('evidence') for g in gates)
        record('deferred_release_gates', ready, deferred)
    from content_review_audit import collect
    try:
        review = collect()
        record("shipped_content_and_audio_review", review['summary']['releaseReady'], review['summary'])
    except (ValueError, KeyError, OSError, TypeError, AttributeError, wave.Error, zipfile.BadZipFile) as error:
        record("shipped_content_and_audio_review", False, str(error))
    record("native_and_store_evidence", False, "Windows/Android physical-device audio, accessibility, signing ownership and all applicable P0 acceptance require separately reviewed evidence. iOS and its four migration directions are excluded from this milestone by user decision ADR-105; exclusion is not a native PASS.")

    artifact = None
    if args.apk and args.aapt:
        apk = args.apk.resolve(strict=True)
        manifest = subprocess.run([str(args.aapt), "dump", "xmltree", str(apk), "AndroidManifest.xml"], capture_output=True, text=True, check=True).stdout
        permission_dump = subprocess.run([str(args.aapt), "dump", "permissions", str(apk)], capture_output=True, text=True, check=True).stdout
        names = re.findall(r"uses-permission(?:-sdk-\d+)?: name='([^']+)'", permission_dump)
        allowed = {"android.permission.INTERNET", "android.permission.RECORD_AUDIO", identity + ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"}
        record("apk_permissions", bool(names) and set(names) <= allowed and "android.permission.RECORD_AUDIO" in names, sorted(names))
        record("apk_backup_disabled", bool(re.search(r"android:allowBackup.*(?:0x0|false)\s*$", manifest, re.M)), "Compiled manifest must disable backup.")
        share = subprocess.run([str(args.aapt), "dump", "xmltree", str(apk), "res/xml/microsoft_maui_essentials_fileprovider_file_paths.xml"], capture_output=True, text=True, check=True).stdout
        record("apk_share_scope", '"sharing/"' in share and len(re.findall(r"E: \S+-path", share)) == 1 and "E: cache-path" in share,
               "Compiled provider exposes exactly cache/sharing.")
        artifact = {"name": apk.name, "sha256": hashlib.sha256(apk.read_bytes()).hexdigest(), "bytes": apk.stat().st_size}
    else:
        record("apk_inspection", False, "Provide --apk and --aapt to inspect the final compiled manifest/provider.")

    blocked = sum(c["status"] != "PASS" for c in checks)
    result = {"releaseReady": not blocked, "blocked": blocked, "checks": checks, "artifact": artifact, "dependencies": dependencies}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    for check in checks:
        print(check["status"] + " " + check["check"])
    print("Release readiness: " + ("BLOCKED" if blocked else "PASS"))
    return 2 if blocked else 0


if __name__ == "__main__":
    raise SystemExit(main())
