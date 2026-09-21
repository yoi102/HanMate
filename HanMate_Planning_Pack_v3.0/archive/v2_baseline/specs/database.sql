-- HanMate 数据库 v1 草案。此文件只做结构/约束校验，未集成 MAUI。
-- 正式应用必须逐连接启用外键；迁移/权限/确认/文件事务由服务层实现。
PRAGMA foreign_keys = ON;
BEGIN IMMEDIATE;
CREATE TABLE app_state (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1),
 data_epoch INTEGER NOT NULL DEFAULT 1 CHECK(data_epoch>=1),
 catalog_version TEXT
);
INSERT INTO app_state(singleton,data_epoch) VALUES(1,1);
CREATE TABLE content (
 id TEXT PRIMARY KEY NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN ('word','text','poem')),
 origin TEXT NOT NULL CHECK(origin IN ('personal','builtin','builtinSnapshot')),
 title TEXT NOT NULL,
 body_json TEXT NOT NULL CHECK(json_valid(body_json)),
 row_revision INTEGER NOT NULL CHECK(row_revision>=1),
 content_revision INTEGER NOT NULL CHECK(content_revision>=1),
 annotation_revision INTEGER NOT NULL CHECK(annotation_revision>=1),
 metadata_revision INTEGER NOT NULL CHECK(metadata_revision>=1),
 semantic_fingerprint TEXT NOT NULL CHECK(length(semantic_fingerprint)=64),
 created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL
);
CREATE INDEX idx_content_kind_title ON content(kind,title,id);
CREATE TABLE pinyin_item (
 id TEXT PRIMARY KEY NOT NULL, group_code TEXT NOT NULL,
 display TEXT NOT NULL, sort_order INTEGER NOT NULL,
 body_json TEXT NOT NULL CHECK(json_valid(body_json))
);
CREATE TABLE playback_target (
 id TEXT PRIMARY KEY NOT NULL,
 content_id TEXT REFERENCES content(id) ON DELETE CASCADE,
 pinyin_item_id TEXT REFERENCES pinyin_item(id) ON DELETE CASCADE,
 role TEXT NOT NULL CHECK(role IN ('unit','segment','pinyinItem')),
 text_hash TEXT NOT NULL CHECK(length(text_hash)=64),
 pronunciation_hash TEXT NOT NULL CHECK(length(pronunciation_hash)=64),
 CHECK((role='pinyinItem' AND pinyin_item_id IS NOT NULL AND content_id IS NULL)
    OR (role IN ('unit','segment') AND content_id IS NOT NULL AND pinyin_item_id IS NULL))
);
CREATE INDEX idx_target_content ON playback_target(content_id);
CREATE TABLE audio_asset (
 id TEXT PRIMARY KEY NOT NULL,
 sha256 TEXT NOT NULL CHECK(length(sha256)=64),
 relative_path TEXT NOT NULL,
 byte_length INTEGER NOT NULL CHECK(byte_length>0),
 duration_ms INTEGER NOT NULL CHECK(duration_ms>0),
 container TEXT NOT NULL CHECK(container IN ('wav','mp3','m4a')),
 codec TEXT NOT NULL, sample_rate INTEGER NOT NULL CHECK(sample_rate>0),
 channels INTEGER NOT NULL CHECK(channels BETWEEN 1 AND 2),
 origin TEXT NOT NULL CHECK(origin IN ('user','catalog'))
);
CREATE INDEX idx_asset_hash ON audio_asset(sha256);
CREATE TABLE audio_binding (
 id TEXT PRIMARY KEY NOT NULL,
 target_id TEXT NOT NULL REFERENCES playback_target(id) ON DELETE CASCADE,
 asset_id TEXT NOT NULL REFERENCES audio_asset(id) ON DELETE RESTRICT,
 bound_text_hash TEXT NOT NULL CHECK(length(bound_text_hash)=64),
 bound_pronunciation_hash TEXT NOT NULL CHECK(length(bound_pronunciation_hash)=64),
 review_state TEXT NOT NULL CHECK(review_state IN ('confirmed','needsReview')),
 source_role TEXT NOT NULL CHECK(source_role IN ('standard','user','imported')),
 label TEXT NOT NULL DEFAULT '',
 UNIQUE(target_id,id)
);
CREATE INDEX idx_binding_asset ON audio_binding(asset_id);
CREATE TABLE audio_preference (
 target_id TEXT PRIMARY KEY NOT NULL REFERENCES playback_target(id) ON DELETE CASCADE,
 binding_id TEXT NOT NULL,
 FOREIGN KEY(target_id,binding_id) REFERENCES audio_binding(target_id,id) ON DELETE CASCADE
);
CREATE TABLE favorite_folder (
 id TEXT PRIMARY KEY NOT NULL,
 name TEXT NOT NULL CHECK(length(trim(name))>0),
 name_key TEXT NOT NULL, description TEXT NOT NULL DEFAULT '',
 sort_order INTEGER NOT NULL DEFAULT 0,
 system_role TEXT CHECK(system_role IS NULL OR system_role='default')
);
CREATE UNIQUE INDEX ux_folder_default ON favorite_folder(system_role) WHERE system_role IS NOT NULL;
CREATE UNIQUE INDEX ux_folder_name ON favorite_folder(name_key) WHERE system_role IS NULL;
CREATE TABLE favorite_item (
 folder_id TEXT NOT NULL REFERENCES favorite_folder(id) ON DELETE CASCADE,
 content_id TEXT NOT NULL REFERENCES content(id) ON DELETE CASCADE,
 sort_order INTEGER NOT NULL DEFAULT 0, added_at_utc TEXT NOT NULL,
 PRIMARY KEY(folder_id,content_id)
);
CREATE TABLE user_settings (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1),
 schema_version INTEGER NOT NULL CHECK(schema_version=1),
 body_json TEXT NOT NULL CHECK(json_valid(body_json))
);
CREATE TABLE search_index (
 content_id TEXT PRIMARY KEY NOT NULL REFERENCES content(id) ON DELETE CASCADE,
 hanzi_key TEXT NOT NULL, pinyin_joined TEXT NOT NULL, pinyin_separated TEXT NOT NULL,
 tone_joined TEXT NOT NULL, tone_separated TEXT NOT NULL,
 syllables_json TEXT NOT NULL CHECK(json_valid(syllables_json)),
 frequency_rank INTEGER, index_version INTEGER NOT NULL CHECK(index_version>=1)
);
CREATE INDEX idx_search_hanzi ON search_index(hanzi_key,content_id);
CREATE INDEX idx_search_pinyin ON search_index(pinyin_joined,content_id);
CREATE TABLE import_receipt (
 package_id TEXT PRIMARY KEY NOT NULL,
 package_fingerprint TEXT NOT NULL CHECK(length(package_fingerprint)=64),
 archive_sha256 TEXT NOT NULL CHECK(length(archive_sha256)=64),
 committed_at_utc TEXT NOT NULL,
 result_json TEXT NOT NULL CHECK(json_valid(result_json))
);
CREATE TABLE import_mapping (
 source_namespace TEXT NOT NULL,
 entity_type TEXT NOT NULL CHECK(entity_type IN ('content','unit','segment','token','target','folder','asset','binding')),
 source_id TEXT NOT NULL, source_fingerprint TEXT NOT NULL,
 local_id TEXT NOT NULL,
 first_package_id TEXT NOT NULL REFERENCES import_receipt(package_id) ON DELETE RESTRICT,
 PRIMARY KEY(source_namespace,entity_type,source_id,source_fingerprint)
);
CREATE TABLE draft (
 id TEXT PRIMARY KEY NOT NULL, target_content_id TEXT,
 draft_kind TEXT NOT NULL CHECK(draft_kind IN ('content','audio')),
 body_json TEXT NOT NULL CHECK(json_valid(body_json)), updated_at_utc TEXT NOT NULL
);
-- 租约可以保护尚未写入资产表的文件，因此 sha256 不外键依赖 audio_asset。
CREATE TABLE file_lease (
 lease_id TEXT PRIMARY KEY NOT NULL, sha256 TEXT NOT NULL,
 reason TEXT NOT NULL CHECK(reason IN ('playback','export','draft','import')),
 expires_at_utc TEXT NOT NULL
);
CREATE INDEX idx_lease_hash ON file_lease(sha256);
PRAGMA user_version=1;
COMMIT;
