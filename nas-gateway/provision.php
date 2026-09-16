<?php

require_once dirname(__FILE__) . '/common.php';

header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_verify_assertion('provision');


$projectHubRoot = '/mnt/HDD1/ProjectHub';
$objectsDir = $projectHubRoot . '/objects';
$sha256Dir = $objectsDir . '/sha256';
$stagingDir = $projectHubRoot . '/staging';


$directories = array(
    $projectHubRoot,
    $objectsDir,
    $sha256Dir,
    $stagingDir
);

foreach ($directories as $directory) {
    if (!file_exists($directory)) {
        if (!@mkdir($directory, 0750)) {
            $lastError = error_get_last();

            http_response_code(500);
            echo json_encode(array(
                'error' => 'directory_create_failed',
                'path' => $directory,
                'php_error' => $lastError ? $lastError['message'] : null
            ));
            exit;
        }
    }

    if (!is_dir($directory)) {
        projecthub_json_error(500, 'storage_path_invalid');
    }

    if (is_link($directory)) {
        projecthub_json_error(500, 'symlink_not_allowed');
    }
}

$testFile = $stagingDir . '/.projecthub-write-test-' .
    preg_replace('/[^A-Za-z0-9._-]/', '_', $claims['jti']);

$handle = @fopen($testFile, 'x');

if ($handle === false) {
    projecthub_json_error(500, 'write_test_create_failed');
}

$testData = "ProjectHub";

$written = fwrite($handle, $testData);

if ($written !== strlen($testData)) {
    fclose($handle);
    @unlink($testFile);
    projecthub_json_error(500, 'write_test_failed');
}

fflush($handle);
fclose($handle);


$readBack = @file_get_contents($testFile);

if ($readBack !== $testData) {
    @unlink($testFile);
    projecthub_json_error(500, 'write_test_verify_failed');
}

if (!@unlink($testFile)) {
    projecthub_json_error(500, 'write_test_cleanup_failed');
}


echo json_encode(array(
    'state' => 'ready',
    'storage_scope' => $claims['storage_scope'],
    'project_id' => $claims['project_id']
));