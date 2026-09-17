<?php

require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_upload_claims('upload');
$sessionDir = projecthub_upload_session_dir($claims['upload_session_id']);
$objectHash = strtolower($claims['object_hash']);
$sizeBytes = intval($claims['size_bytes']);
$objectPath = projecthub_safe_path(projecthub_storage_root(), 'objects/sha256/' . substr($objectHash, 0, 2) . '/' . $objectHash);

if (file_exists($objectPath)) {
    if (is_link($objectPath) || !is_file($objectPath) || filesize($objectPath) !== $sizeBytes) {
        projecthub_json_error(409, 'object_conflict');
    }
    $stagingCleaned = true;
    if (is_dir($sessionDir) || file_exists($sessionDir)) {
        if (is_link($sessionDir) || !is_dir($sessionDir)) { projecthub_json_error(500, 'staging_path_invalid'); }
        $metadataPath = $sessionDir . '/session.json';
        $metadata = is_file($metadataPath) ? json_decode(file_get_contents($metadataPath), true) : null;
        if (!is_array($metadata) || strtolower($metadata['object_hash'] ?? '') !== $objectHash || intval($metadata['size_bytes'] ?? -1) !== $sizeBytes || ($metadata['project_id'] ?? '') !== $claims['project_id'] || ($metadata['workstation_id'] ?? '') !== $claims['workstation_id']) { projecthub_json_error(409, 'staging_session_conflict'); }
        $stagingCleaned = projecthub_cleanup_session($sessionDir, glob($sessionDir . '/*.part'));
        if (!$stagingCleaned) { projecthub_json_error(500, 'staging_cleanup_failed'); }
    }
    $namedPath = projecthub_create_named_alias($claims, $objectPath, $objectHash);
    echo json_encode(array('state' => 'complete', 'already_present' => true, 'staging_cleaned' => $stagingCleaned, 'upload_session_id' => $claims['upload_session_id'], 'object_hash' => $objectHash, 'size_bytes' => $sizeBytes, 'named_path' => $namedPath));
    exit;
}

if (!is_dir($sessionDir) && !@mkdir($sessionDir, 0750, true)) {
    projecthub_json_error(500, 'staging_create_failed');
}
if (is_link($sessionDir) || !is_dir($sessionDir)) {
    projecthub_json_error(500, 'staging_path_invalid');
}

$metadata = array('object_hash' => $objectHash, 'size_bytes' => $sizeBytes, 'project_id' => $claims['project_id'], 'workstation_id' => $claims['workstation_id']);
if (@file_put_contents($sessionDir . '/session.json', json_encode($metadata), LOCK_EX) === false) {
    projecthub_json_error(500, 'session_metadata_write_failed');
}

echo json_encode(array('state' => 'uploading', 'already_present' => false, 'upload_session_id' => $claims['upload_session_id'], 'object_hash' => $objectHash, 'size_bytes' => $sizeBytes));
