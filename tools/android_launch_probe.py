"""Measure repeated Android activity starts on an isolated, already installed test device.

Force-stop never clears app data. This measures ActivityManager's TotalTime, not
interactive readiness, and records process PSS after each start. Use only an
explicit emulator serial unless a physical test device has been authorized.
"""

import argparse
import json
from pathlib import Path
import re
import statistics
import subprocess
import time


PACKAGE = "com.companyname.hanmate.app"
ACTIVITY = f"{PACKAGE}/crc64278c67d316dcb1de.MainActivity"


def run(adb: str, serial: str, *arguments: str) -> str:
    process = subprocess.run([adb, "-s", serial, *arguments], check=True,
                             capture_output=True, text=True, encoding="utf-8", timeout=90)
    return process.stdout


def percentile(samples: list[int], fraction: float) -> float:
    ordered = sorted(samples)
    index = (len(ordered) - 1) * fraction
    low = int(index)
    high = min(low + 1, len(ordered) - 1)
    return round(ordered[low] + (ordered[high] - ordered[low]) * (index - low), 1)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default=r"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe")
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--cycles", type=int, default=30)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not args.serial.startswith("emulator-"):
        parser.error("Only an isolated emulator is accepted by this probe")
    if not 1 <= args.cycles <= 100:
        parser.error("cycles must be between 1 and 100")
    if PACKAGE not in run(args.adb, args.serial, "shell", "pm", "list", "packages", PACKAGE):
        raise RuntimeError("HanMate is not installed")
    actual_activity = run(args.adb, args.serial, "shell", "cmd", "package", "resolve-activity", "--brief", PACKAGE)
    if ACTIVITY not in actual_activity:
        raise RuntimeError("Launch activity differs from the expected installed package")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    rows = []
    for number in range(1, args.cycles + 1):
        run(args.adb, args.serial, "shell", "am", "force-stop", PACKAGE)
        launch = run(args.adb, args.serial, "shell", "am", "start", "-W", "-n", ACTIVITY)
        match = re.search(r"^TotalTime:\s*(\d+)$", launch, re.MULTILINE)
        if not match:
            raise RuntimeError(f"Start {number} had no TotalTime: {launch}")
        pid = run(args.adb, args.serial, "shell", "pidof", PACKAGE).strip()
        if not pid:
            raise RuntimeError(f"App exited after start {number}")
        time.sleep(0.2)
        memory = run(args.adb, args.serial, "shell", "dumpsys", "meminfo", PACKAGE)
        pss = re.search(r"^\s*TOTAL PSS:\s*(\d+)", memory, re.MULTILINE)
        if not pss:
            raise RuntimeError(f"PSS missing after start {number}")
        rows.append({"cycle": number, "activityTotalTimeMs": int(match.group(1)),
                     "processPssKiB": int(pss.group(1)), "pid": pid})
        args.output.write_text(json.dumps({"scope": "emulator activity startup and immediate PSS, not UI readiness",
                                           "serial": args.serial, "samples": rows}, indent=2) + "\n", encoding="utf-8")
        print(f"{number}/{args.cycles}: {match.group(1)} ms, PSS {pss.group(1)} KiB", flush=True)
    report = json.loads(args.output.read_text(encoding="utf-8"))
    report["summary"] = {"count": len(rows),
                         "activityTotalTimeP50Ms": statistics.median(r["activityTotalTimeMs"] for r in rows),
                         "activityTotalTimeP95Ms": percentile([r["activityTotalTimeMs"] for r in rows], .95),
                         "processPssP50KiB": statistics.median(r["processPssKiB"] for r in rows),
                         "processPssMaxKiB": max(r["processPssKiB"] for r in rows)}
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
