<?php

require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_upload_claims('delete');
$body = projecthub_json_body();
$deleteCanonical = isset($body['delete_canonical']) && $body['delete_canonical'] === true;
$objectHash = strtolower($claims['object_hash']);
$namedPath = projecthub_named_object_path($claims, $objectHash);
$objectPath = projecthub_canonical_object_path($objectHash);
$aliasDeleted = false;
$canonicalDeleted = false;
$alreadyDeleted = false;

if ($namedPath !== null && file_exists($namedPath)) {
    if (is_link($namedPath) || !is_file($namedPath)) {
        projecthub_json_error(409, 'named_path_conflict');
    }
    if (!@unlink($namedPath)) {
        projecthub_json_error(502, 'named_alias_delete_failed');
    }
    $aliasDeleted = true;
} else {
    $alreadyDeleted = true;
}

if ($deleteCanonical) {
    if (file_exists($objectPath)) {
        if (is_link($objectPath) || !is_file($objectPath)) {
            projecthub_json_error(409, 'object_path_conflict');
        }
        if (!@unlink($objectPath)) {
            projecthub_json_error(502, 'canonical_object_delete_failed');
        }
        $canonicalDeleted = true;
    } else {
        $alreadyDeleted = true;
    }
}

echo json_encode(array(
    'state' => 'deleted',
    'already_deleted' => $alreadyDeleted,
    'alias_deleted' => $aliasDeleted,
    'canonical_deleted' => $canonicalDeleted,
    'named_path' => $namedPath === null ? null : 'files/' . $claims['project_id'] . '/' . $claims['relative_path'],
    'object_path' => 'objects/sha256/' . substr($objectHash, 0, 2) . '/' . $objectHash
));
