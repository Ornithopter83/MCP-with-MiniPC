<?php
require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { projecthub_json_error(405, 'method_not_allowed'); }
$claims = projecthub_upload_claims('cleanup');
$sessionDir = projecthub_upload_session_dir($claims['upload_session_id']);
if (!is_dir($sessionDir) || is_link($sessionDir)) { echo json_encode(array('state' => 'already_clean', 'upload_session_id' => $claims['upload_session_id'], 'freed_bytes' => 0)); exit; }
$metadataPath = $sessionDir . '/session.json';
$metadata = is_file($metadataPath) ? json_decode(file_get_contents($metadataPath), true) : null;
if (!is_array($metadata) || strtolower($metadata['object_hash'] ?? '') !== strtolower($claims['object_hash']) || intval($metadata['size_bytes'] ?? -1) !== intval($claims['size_bytes']) || ($metadata['project_id'] ?? '') !== $claims['project_id'] || ($metadata['workstation_id'] ?? '') !== $claims['workstation_id']) { projecthub_json_error(409, 'staging_session_conflict'); }
$parts = glob($sessionDir . '/*.part');
$freed = 0;
foreach ($parts as $part) { if (is_file($part) && !is_link($part)) { $freed += filesize($part); } }
if (!projecthub_cleanup_session($sessionDir, $parts)) { projecthub_json_error(500, 'staging_cleanup_failed'); }
echo json_encode(array('state' => 'cleaned', 'upload_session_id' => $claims['upload_session_id'], 'deleted_files' => count($parts) + 2, 'freed_bytes' => $freed));
