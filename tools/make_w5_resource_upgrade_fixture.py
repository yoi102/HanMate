"""Derive isolated learning-resource upgrades from the planning fixtures.

The output is engineering-only content. It never reads or modifies app data.
"""

import argparse
import copy
import hashlib
import io
import json
import math
from pathlib import Path
import struct
import uuid
import wave
import zipfile


ROOT = Path(__file__).resolve().parents[1]
RESOURCE_ID = uuid.uuid5(uuid.NAMESPACE_URL, "hanmate-w5-isolated-resource-upgrade-20260923")


def encoded(value):
    return json.dumps(value, ensure_ascii=False, indent=2).encode("utf-8")


def load(path, member):
    with zipfile.ZipFile(path) as archive:
        return json.loads(archive.read(member))


def test_tone():
    """One second of synthetic 440 Hz audio; never represent this as spoken teaching audio."""
    samples = b"".join(struct.pack("<h", round(6000 * math.sin(2 * math.pi * 440 * index / 8000)))
                       for index in range(8000))
    output = io.BytesIO()
    with wave.open(output, "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(8000)
        audio.writeframes(samples)
    return output.getvalue()


def pronunciation_hash(unit):
    tokens = [[token["start"], token["length"],
               token["pinyin"]["base"] if token["pinyin"] else None,
               token["pinyin"]["tone"] if token["pinyin"] else None,
               token["pinyin"]["erhua"] if token["pinyin"] else None]
              for token in unit["tokens"]]
    value = json.dumps(tokens, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    sample = ROOT / "HanMate_Planning_Pack_v3.0/examples/sample-learning.hanresource"
    content_sample = ROOT / "HanMate_Planning_Pack_v3.0/examples/sample-content.hanpack"
    manifest = load(sample, "manifest.json")
    descriptor = load(sample, "resource.json")
    contents = load(sample, "contents.json")
    extra = next(item for item in load(content_sample, "contents.json")["contents"]
                 if item["kind"] == "word" and item["title"] == "中国")

    old_id = descriptor["resourceId"]
    graph_ids = {old_id: str(RESOURCE_ID)}
    for entry in descriptor["entries"]:
        graph_ids[entry["contentId"]] = str(uuid.uuid5(RESOURCE_ID, entry["entryId"]))
    graph_ids[extra["id"]] = str(uuid.uuid5(RESOURCE_ID, "entry-5"))

    def remap(value):
        if isinstance(value, dict):
            return {key: remap(item) for key, item in value.items()}
        if isinstance(value, list):
            return [remap(item) for item in value]
        if isinstance(value, str):
            try:
                parsed = uuid.UUID(value)
            except ValueError:
                return value
            return graph_ids.get(value, str(uuid.uuid5(RESOURCE_ID, str(parsed))))
        return value

    base_descriptor = remap(descriptor)
    base_descriptor["names"] = {
        "zh-Hans": "W5隔离升级资源", "ja": "W5検証リソース", "en": "W5 isolated upgrade resource"
    }
    base_contents = remap(contents)
    new_word = remap(extra)
    new_word["id"] = graph_ids[extra["id"]]
    new_word["origin"] = "resource"
    new_word["source"]["resourceId"] = str(RESOURCE_ID)
    new_word["source"]["entryId"] = "entry-5"

    with zipfile.ZipFile(sample) as archive:
        audio_index = archive.read("audio/index.json")

    results = []
    for version in ("1.0.0", "1.0.1", "1.0.2", "1.0.3"):
        next_descriptor = copy.deepcopy(base_descriptor)
        next_contents = copy.deepcopy(base_contents)
        next_descriptor["version"] = version
        for item in next_contents["contents"]:
            item["source"]["resourceVersion"] = version
        if version != "1.0.0":
            added = copy.deepcopy(new_word)
            added["source"]["resourceVersion"] = version
            next_contents["contents"].append(added)
            next_descriptor["entries"].append({"entryId": "entry-5", "contentId": added["id"]})
        if version == "1.0.3":
            next_contents["contents"][0]["title"] = "“在”表示位置（工程新版）"

        files = {"audio/index.json": audio_index, "contents.json": encoded(next_contents),
                 "resource.json": encoded(next_descriptor)}
        next_manifest = copy.deepcopy(manifest)
        next_manifest["packageId"] = str(uuid.uuid5(RESOURCE_ID, "package-" + version))
        next_manifest["counts"]["contents"] = len(next_contents["contents"])
        if version in ("1.0.2", "1.0.3"):
            unit = next_contents["contents"][0]["textUnits"][0]
            tone = test_tone()
            audio_hash = hashlib.sha256(tone).hexdigest()
            path = f"audio/{audio_hash}.wav"
            asset_id = str(uuid.uuid5(RESOURCE_ID, "synthetic-tone-asset"))
            binding_id = str(uuid.uuid5(RESOURCE_ID, "synthetic-tone-binding"))
            preference = {"targetId": unit["id"], "bindingId": binding_id}
            audio = {"schemaVersion": 1,
                     "assets": [{"id": asset_id, "origin": "catalog", "storage": "package",
                                 "sha256": audio_hash, "byteLength": len(tone), "durationMs": 1000,
                                 "container": "wav", "codec": "pcm_s16le", "sampleRate": 8000,
                                 "channels": 1, "path": path, "catalogRef": None}],
                     "bindings": [{"id": binding_id, "targetId": unit["id"], "assetId": asset_id,
                                   "boundTextHash": hashlib.sha256(unit["text"].encode("utf-8")).hexdigest(),
                                   "boundPronunciationHash": pronunciation_hash(unit),
                                   "reviewState": "confirmed", "sourceRole": "standard",
                                   "label": "Synthetic engineering tone, not speech"}],
                     "preferences": [preference]}
            next_descriptor["audioBindingIds"] = [binding_id]
            next_descriptor["audioDefaults"] = [preference]
            next_manifest["counts"]["audioAssets"] = 1
            next_manifest["counts"]["audioBindings"] = 1
            next_manifest["files"].append({"path": path, "byteLength": len(tone), "sha256": audio_hash})
            files[path] = tone
            files["audio/index.json"] = encoded(audio)
            files["resource.json"] = encoded(next_descriptor)
        for entry in next_manifest["files"]:
            data = files[entry["path"]]
            entry["byteLength"] = len(data)
            entry["sha256"] = hashlib.sha256(data).hexdigest()

        path = args.output_dir / ("w5-learning-" + version + ".hanresource")
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in files.items():
                archive.writestr(name, data)
            archive.writestr("manifest.json", encoded(next_manifest))
        results.append({"version": version, "path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                        "contents": len(next_contents["contents"])})
    print(json.dumps({"resourceId": str(RESOURCE_ID), "packages": results}, ensure_ascii=False))


if __name__ == "__main__":
    main()
