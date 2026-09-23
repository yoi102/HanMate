"""Repeat the long-definition UI path on an already open Android dictionary page.

This tool does not install an APK, change preferences outside the exercised UI,
or clear app data. It saves UI snapshots and a failure log under TEMP by default.
"""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET


PACKAGE = "com.companyname.hanmate.app"
ROOT = Path(__file__).resolve().parent


def run(command: list[str], timeout: int = 45) -> str:
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{' '.join(command)} failed: {result.stderr.strip()}")
    return result.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default=r"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe")
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--cycles", type=int, default=10)
    parser.add_argument("--out", type=Path, default=Path(os.environ.get("TEMP", ".")) / "HanMate-dictionary-stress")
    args = parser.parse_args()
    if not 1 <= args.cycles <= 100:
        parser.error("--cycles must be between 1 and 100")
    args.out.mkdir(parents=True, exist_ok=True)
    adb = [args.adb, "-s", args.serial]
    actions: list[dict[str, object]] = []

    def pid() -> str:
        value = run(adb + ["shell", "pidof", PACKAGE]).strip()
        if not value:
            raise RuntimeError("HanMate is not running")
        return value

    def act(action: str, target: str | None = None) -> ET.Element:
        before = pid()
        command = [sys.executable, "-X", "utf8", str(ROOT / "android_acceptance_ui.py"),
                   "--adb", args.adb, "--serial", args.serial, "--out", str(args.out / "ui"), action]
        if target:
            command.append(target)
        output = run(command, timeout=75)
        after = pid()
        if before != after:
            raise RuntimeError(f"HanMate process changed during {action}: {before} -> {after}")
        first = output.splitlines()[0]
        if not first.startswith("snapshot: "):
            raise RuntimeError("UI helper did not return a snapshot")
        snapshot = Path(first.removeprefix("snapshot: ").strip())
        root = ET.parse(snapshot.with_suffix(".xml")).getroot()
        actions.append({"time": time.time(), "action": action, "target": target,
                        "pid": after, "snapshot": str(snapshot)})
        (args.out / "actions.json").write_text(json.dumps(actions, ensure_ascii=False, indent=2), encoding="utf-8")
        return root

    def has(root: ET.Element, suffix: str) -> bool:
        return any((node.get("resource-id") or "").endswith(suffix) for node in root.iter("node"))

    try:
        root = act("snapshot")
        if not has(root, ":id/Dictionary.Sections"):
            raise RuntimeError("Open the long dictionary entry before running this tool")
        for cycle in range(args.cycles):
            for _ in range(20):
                if has(root, ":id/Dictionary.ExpandDefinitions"):
                    break
                root = act("scroll-down", f"id:{PACKAGE}:id/Dictionary.Sections")
            else:
                raise RuntimeError("Expand/collapse control was not found after 20 scrolls")
            root = act("tap", "Dictionary.ExpandDefinitions")
            root = act("tap", "desc:Dictionary.Pinyin")
            root = act("tap", "desc:Dictionary.Pinyin")
            for _ in range(20):
                if has(root, ":id/Dictionary.ExpandDefinitions"):
                    break
                root = act("scroll-down", f"id:{PACKAGE}:id/Dictionary.Sections")
            else:
                raise RuntimeError("Collapse control disappeared")
            root = act("tap", "Dictionary.ExpandDefinitions")
            if not has(root, ":id/Dictionary.ExpandDefinitions"):
                raise RuntimeError("Collapse lost the control")
            print(f"PASS cycle {cycle + 1}/{args.cycles} pid={pid()}", flush=True)
        return 0
    except Exception as error:
        (args.out / "failure.txt").write_text(str(error), encoding="utf-8")
        try:
            (args.out / "failure-logcat.txt").write_text(
                run(adb + ["logcat", "-d", "-t", "3000"], timeout=30), encoding="utf-8")
        except Exception:
            pass
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
