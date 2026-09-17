<?php
require_once dirname(__FILE__) . '/common.php';
if ($_SERVER['REQUEST_METHOD'] !== 'GET') { projecthub_json_error(405, 'method_not_allowed'); }
$claims = projecthub_upload_claims('download');
$hash = strtolower($claims['object_hash']);
$path = projecthub_safe_path(projecthub_storage_root(), 'objects/sha256/' . substr($hash, 0, 2) . '/' . $hash);
if (!is_file($path) || is_link($path) || filesize($path) !== intval($claims['size_bytes'])) { projecthub_json_error(404, 'object_not_found'); }
header('Content-Type: application/octet-stream'); header('Content-Length: ' . filesize($path));
readfile($path);
