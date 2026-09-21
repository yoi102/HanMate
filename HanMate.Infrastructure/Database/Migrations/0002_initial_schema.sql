-- HanMate initial database schema v2.
-- Transaction, foreign_keys and user_version are controlled by SqliteMigrationRunner.
-- HanMate 数据库 v2 草案。此文件只做结构/约束校验，未集成 MAUI。
-- 正式应用必须逐连接启用外键；迁移/权限/确认/文件事务由服务层实现。
CREATE TABLE app_state (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1),
 data_epoch INTEGER NOT NULL DEFAULT 1 CHECK(data_epoch>=1),
 catalog_version TEXT
);
INSERT INTO app_state(singleton,data_epoch) VALUES(1,1);
CREATE TABLE content (
 id TEXT PRIMARY KEY NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN ('word','text','grammar','poem')),
 origin TEXT NOT NULL CHECK(origin IN ('personal','builtin','builtinSnapshot','resource','retained')),
 title TEXT NOT NULL,
 body_json TEXT NOT NULL CHECK(json_valid(body_json)),
 row_revision INTEGER NOT NULL CHECK(row_revision>=1),
 membership_revision INTEGER NOT NULL DEFAULT 1 CHECK(membership_revision>=1),
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
 row_revision INTEGER NOT NULL DEFAULT 1 CHECK(row_revision>=1),
 body_json TEXT NOT NULL CHECK(json_valid(body_json))
);
CREATE TABLE search_index (
 content_id TEXT NOT NULL REFERENCES content(id) ON DELETE CASCADE,
 alias_ordinal INTEGER NOT NULL DEFAULT 0 CHECK(alias_ordinal>=0),
 hanzi_key TEXT NOT NULL, pinyin_joined TEXT NOT NULL, pinyin_separated TEXT NOT NULL,
 tone_joined TEXT NOT NULL, tone_separated TEXT NOT NULL,
 syllables_json TEXT NOT NULL CHECK(json_valid(syllables_json)),
 frequency_rank INTEGER, index_version INTEGER NOT NULL CHECK(index_version>=1),
 PRIMARY KEY(content_id,alias_ordinal)
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
-- package 身份与执行身份分离：同一备份可经用户确认多次恢复。
-- 仅在数据提交事务中插入；外部 preparing 日志不能充当已提交证据。
CREATE TABLE import_operation (
 operation_id TEXT PRIMARY KEY NOT NULL,
 package_id TEXT NOT NULL REFERENCES import_receipt(package_id) ON DELETE RESTRICT,
 import_mode TEXT NOT NULL CHECK(import_mode IN ('merge','replace')),
 expected_data_epoch INTEGER NOT NULL CHECK(expected_data_epoch>=1),
 committed_at_utc TEXT NOT NULL,
 result_json TEXT NOT NULL CHECK(json_valid(result_json))
);
CREATE INDEX idx_import_operation_package ON import_operation(package_id,committed_at_utc);
CREATE TABLE import_mapping (
 source_namespace TEXT NOT NULL,
 entity_type TEXT NOT NULL CHECK(entity_type IN ('content','unit','segment','token','target','folder','asset','binding','resource')),
 source_id TEXT NOT NULL, source_fingerprint TEXT NOT NULL,
 local_id TEXT NOT NULL,
 first_package_id TEXT NOT NULL REFERENCES import_receipt(package_id) ON DELETE RESTRICT,
 PRIMARY KEY(source_namespace,entity_type,source_id,source_fingerprint)
);
CREATE TABLE draft (
 id TEXT PRIMARY KEY NOT NULL, target_content_id TEXT,
 draft_kind TEXT NOT NULL CHECK(draft_kind IN ('content','audio')),
 row_revision INTEGER NOT NULL DEFAULT 1 CHECK(row_revision>=1),
 body_json TEXT NOT NULL CHECK(json_valid(body_json)), updated_at_utc TEXT NOT NULL
);
-- 租约可以保护尚未写入资产表的文件，因此 sha256 不外键依赖 audio_asset。
CREATE TABLE file_lease (
 lease_id TEXT PRIMARY KEY NOT NULL, sha256 TEXT NOT NULL,
 reason TEXT NOT NULL CHECK(reason IN ('playback','export','draft','import')),
 expires_at_utc TEXT NOT NULL
);
CREATE INDEX idx_lease_hash ON file_lease(sha256);
-- 资源主数据与投影必须同事务更新；用户撤下状态不是源内容的一部分。
CREATE TABLE installed_resource (
 resource_id TEXT PRIMARY KEY NOT NULL,
 resource_kind TEXT NOT NULL CHECK(resource_kind IN ('learning','dictionary')),
 version TEXT NOT NULL,
 descriptor_json TEXT NOT NULL CHECK(json_valid(descriptor_json)),
 descriptor_sha256 TEXT NOT NULL CHECK(length(descriptor_sha256)=64),
 payload_fingerprint TEXT NOT NULL CHECK(length(payload_fingerprint)=64),
 distribution TEXT NOT NULL CHECK(distribution IN ('bundled','external')),
 is_present INTEGER NOT NULL CHECK(is_present IN (0,1)),
 enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),
 priority INTEGER NOT NULL DEFAULT 0 CHECK(priority>=0),
 row_revision INTEGER NOT NULL DEFAULT 1 CHECK(row_revision>=1),
 CHECK(is_present=1 OR enabled=0)
);
CREATE TABLE resource_entry (
 resource_id TEXT NOT NULL REFERENCES installed_resource(resource_id) ON DELETE RESTRICT,
 entry_id TEXT NOT NULL,
 content_id TEXT NOT NULL UNIQUE REFERENCES content(id) ON DELETE RESTRICT,
 PRIMARY KEY(resource_id,entry_id)
);
CREATE TABLE resource_entry_override (
 resource_id TEXT NOT NULL REFERENCES installed_resource(resource_id) ON DELETE RESTRICT,
 entry_id TEXT NOT NULL,
 removed INTEGER NOT NULL CHECK(removed IN (0,1)),
 updated_at_utc TEXT NOT NULL,
 PRIMARY KEY(resource_id,entry_id)
);
-- override 不外键引用 resource_entry：上游删除条目后仍要保留用户撤下决定。
CREATE TABLE retained_content (
 content_id TEXT PRIMARY KEY NOT NULL REFERENCES content(id) ON DELETE RESTRICT,
 source_resource_id TEXT NOT NULL,
 source_entry_id TEXT NOT NULL,
 source_version TEXT NOT NULL,
 reason TEXT NOT NULL CHECK(reason IN ('favorite','userAudio','draft','explicitKeep','multiple')),
 retained_at_utc TEXT NOT NULL
);
CREATE TABLE resource_operation (
 operation_id TEXT PRIMARY KEY NOT NULL,
 resource_id TEXT NOT NULL,
 operation_type TEXT NOT NULL CHECK(operation_type IN ('install','update','remove','restoreEntry','disable','enable')),
 expected_data_epoch INTEGER NOT NULL,
 committed_at_utc TEXT NOT NULL,
 result_json TEXT NOT NULL CHECK(json_valid(result_json))
);
CREATE INDEX idx_resource_kind_enabled ON installed_resource(resource_kind,enabled,is_present,priority);
CREATE INDEX idx_retained_source ON retained_content(source_resource_id,source_entry_id);
-- 字典资源只能拥有 word；应用验证器同时校验 descriptor 与 ContentDocument 来源。
CREATE TRIGGER resource_entry_dictionary_guard BEFORE INSERT ON resource_entry
WHEN (SELECT resource_kind FROM installed_resource WHERE resource_id=NEW.resource_id)='dictionary'
 AND (SELECT kind FROM content WHERE id=NEW.content_id)<>'word'
BEGIN SELECT RAISE(ABORT,'dictionary accepts word only'); END;
CREATE TRIGGER resource_entry_dictionary_update_guard BEFORE UPDATE ON resource_entry
WHEN (SELECT resource_kind FROM installed_resource WHERE resource_id=NEW.resource_id)='dictionary'
 AND (SELECT kind FROM content WHERE id=NEW.content_id)<>'word'
BEGIN SELECT RAISE(ABORT,'dictionary accepts word only'); END;
CREATE TRIGGER owned_word_kind_guard BEFORE UPDATE OF kind ON content
WHEN NEW.kind<>'word' AND EXISTS(
 SELECT 1 FROM resource_entry e JOIN installed_resource r ON r.resource_id=e.resource_id
 WHERE e.content_id=NEW.id AND r.resource_kind='dictionary')
BEGIN SELECT RAISE(ABORT,'dictionary word cannot change kind'); END;
CREATE TRIGGER resource_kind_immutable BEFORE UPDATE OF resource_kind ON installed_resource
WHEN NEW.resource_kind<>OLD.resource_kind
BEGIN SELECT RAISE(ABORT,'resource kind is immutable'); END;


