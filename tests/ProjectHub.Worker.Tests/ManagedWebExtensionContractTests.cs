using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class ManagedWebExtensionContractTests
{
    [Fact]
    public void EmbeddedManifest_IsManagedBridgeWithoutTabsPermission()
    {
        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProjectHub.Worker.Extension.manifest.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);

        Assert.Equal("0.4.0", document.RootElement.GetProperty("version").GetString());
        var permissions = document.RootElement
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .ToArray();

        Assert.Contains("storage", permissions);
        Assert.DoesNotContain("tabs", permissions);
    }

    [Fact]
    public void EmbeddedBackground_HasNoBrowserTabManagement()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.background.js");

        Assert.False(source.Contains("chrome.tabs.", StringComparison.Ordinal));
        Assert.False(source.Contains("ensure-single-chatgpt-tab", StringComparison.Ordinal));
        Assert.True(source.Contains("fetch-resource-file", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_RequiresManagedRoleAndRuntimeToken()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("projecthub-managed-role", StringComparison.Ordinal));
        Assert.True(source.Contains("projecthub-runtime-token", StringComparison.Ordinal));
        Assert.True(source.Contains("X-ProjectHub-Managed-Token", StringComparison.Ordinal));
        Assert.True(source.Contains("if(!preflightManagedRole||!preflightRuntimeToken)return;", StringComparison.Ordinal));
        Assert.False(source.Contains("ensureManagedSingleChatTab", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_LatchesHiddenSendEvidenceAndReconcilesTurns()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("baselineTurnFingerprints", StringComparison.Ordinal));
        Assert.True(source.Contains("latchedSendEvidence", StringComparison.Ordinal));
        Assert.True(source.Contains("SEND_EVIDENCE_LATCHED", StringComparison.Ordinal));
        Assert.True(source.Contains("SEND_MUTATION_CONFIRMED", StringComparison.Ordinal));
        Assert.True(source.Contains("SEND_TIMEOUT_RECOVERED", StringComparison.Ordinal));
        Assert.True(source.Contains("reconcileConversationAfterSend", StringComparison.Ordinal));
        Assert.True(source.Contains("observeConversationMutation", StringComparison.Ordinal));
        Assert.True(source.Contains("conversationTurns()", StringComparison.Ordinal));
        Assert.True(source.Contains("currentSendConfirmed=taskUserMessageConfirmed()||sendTriggeredForActiveTask", StringComparison.Ordinal));
        Assert.False(source.Contains("messages.length<=beforeMessages.length", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_RecoversAssistantTextAndGeneralDownloadFiles()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("responseTurnAfterPrompt", StringComparison.Ordinal));
        Assert.True(source.Contains("ASSISTANT_CONTAINER_RECONCILED", StringComparison.Ordinal));
        Assert.True(source.Contains("ASSISTANT_TURN_DETECTED", StringComparison.Ordinal));
        Assert.True(source.Contains("ASSISTANT_TEXT_EXTRACTED", StringComparison.Ordinal));
        Assert.True(source.Contains("readyResponseFileCandidates", StringComparison.Ordinal));
        Assert.True(source.Contains("WEB_FILE_DETECTED", StringComparison.Ordinal));
        Assert.True(source.Contains("WEB_FILE_DOWNLOAD_VERIFIED", StringComparison.Ordinal));
        Assert.True(source.Contains("TEXT_WITH_FILES", StringComparison.Ordinal));
        Assert.True(source.Contains("pdf|zip|json|txt|md|csv|docx|xlsx|pptx", StringComparison.Ordinal));
        Assert.True(source.Contains("extension='+EXTENSION_VERSION+' / '+EXTENSION_BUILD", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_WaitsForAttachmentReadinessBeforeSend()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("ATTACHMENT_BYTES_VERIFIED", StringComparison.Ordinal));
        Assert.True(source.Contains("ATTACHMENT_INPUT_SET", StringComparison.Ordinal));
        Assert.True(source.Contains("ATTACHMENT_UI_DETECTED", StringComparison.Ordinal));
        Assert.True(source.Contains("ATTACHMENT_PROCESSING", StringComparison.Ordinal));
        Assert.True(source.Contains("ATTACHMENT_READY", StringComparison.Ordinal));
        Assert.True(source.Contains("ATTACHMENT_READY_TIMEOUT", StringComparison.Ordinal));
        Assert.True(source.Contains("waitForAttachmentReady", StringComparison.Ordinal));
        Assert.True(source.Contains("monitorSendReady(prompt,attachmentState.count)", StringComparison.Ordinal));
        Assert.True(source.Contains("if(attachmentCount>0)", StringComparison.Ordinal));
        Assert.True(source.Contains("첨부 처리/Send 활성화를 계속 기다리는 중", StringComparison.Ordinal));
        Assert.True(source.Contains("generationStartEvidence", StringComparison.Ordinal));
        Assert.True(source.Contains("GENERATION_STARTED_", StringComparison.Ordinal));
        Assert.True(source.Contains("SEND_FALLBACK", StringComparison.Ordinal));
        Assert.True(source.Contains("SEND_CONFIRM_DIAGNOSTIC", StringComparison.Ordinal));
        Assert.True(source.Contains("genericResponseCandidates", StringComparison.Ordinal));
        Assert.True(source.Contains("latestGenericResponseFallback", StringComparison.Ordinal));
        Assert.True(source.Contains("baselineGenericResponseFingerprints", StringComparison.Ordinal));
        Assert.True(source.Contains("role 속성 없이 새 Markdown/본문 영역", StringComparison.Ordinal));
        Assert.True(source.Contains("latchResponseMutation", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_MUTATION_LATCHED", StringComparison.Ordinal));
        Assert.True(source.Contains("currentMutationResponseText", StringComparison.Ordinal));
        Assert.True(source.Contains("WAIT_RESPONSE 이후 실제 텍스트 DOM mutation", StringComparison.Ordinal));
        Assert.True(source.Contains("function responseText(value)", StringComparison.Ordinal));
        Assert.True(source.Contains("replace(/\\r\\n?/g,'\\n')", StringComparison.Ordinal));
        Assert.True(source.Contains("responseText(response.element?.innerText", StringComparison.Ordinal));
        Assert.True(source.Contains("responseText(assistantElement?.innerText", StringComparison.Ordinal));
        Assert.True(source.Contains("responseText(genericItem.element?.innerText", StringComparison.Ordinal));
        Assert.True(source.Contains("let best=responseText(mutationResponseText)", StringComparison.Ordinal));
        Assert.True(source.Contains("activeCorrelationKey", StringComparison.Ordinal));
        Assert.True(source.Contains("activeKeyMarker", StringComparison.Ordinal));
        Assert.True(source.Contains("correlationResponseRoot", StringComparison.Ordinal));
        Assert.True(source.Contains("currentCorrelatedResponseText", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_KEY_MATCHED", StringComparison.Ordinal));
        Assert.True(source.Contains("correlationKey:activeCorrelationKey||null", StringComparison.Ordinal));
        Assert.True(source.Contains("activeCorrelationKey?correlationResponseRoot()", StringComparison.Ordinal));
        Assert.True(source.Contains("fallbackSubmitComposer", StringComparison.Ordinal));
        Assert.True(source.Contains("form.requestSubmit()", StringComparison.Ordinal));
        Assert.False(source.Contains("new PointerEvent('pointerdown'", StringComparison.Ordinal));
        Assert.False(source.Contains("SEND_CONFIRM_TIMEOUT", StringComparison.Ordinal));

        var assignmentIndex = source.IndexOf("input.files=transfer.files;", StringComparison.Ordinal);
        var countValidationIndex = source.IndexOf("if(assignedFileCount!==attachments.length)", StringComparison.Ordinal);
        var inputEventIndex = source.IndexOf("input.dispatchEvent(new Event('input'", StringComparison.Ordinal);
        var changeEventIndex = source.IndexOf("input.dispatchEvent(new Event('change'", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0);
        Assert.True(countValidationIndex > assignmentIndex);
        Assert.True(inputEventIndex > countValidationIndex);
        Assert.True(changeEventIndex > inputEventIndex);
        Assert.False(source.Contains("file input count mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedContent_RollsOverFatalChatUiAndReattachesSameTask()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("pageFatalUiFailure", StringComparison.Ordinal));
        Assert.True(source.Contains("stream recovery polling timed out", StringComparison.Ordinal));
        Assert.True(source.Contains("CONVERSATION_ROLLOVER_TRIGGER", StringComparison.Ordinal));
        Assert.True(source.Contains("/rollover')", StringComparison.Ordinal));
        Assert.True(source.Contains("rollover?role=", StringComparison.Ordinal));
        Assert.True(source.Contains("/rollover/attach", StringComparison.Ordinal));
        Assert.True(source.Contains("bootstrapRolloverTask", StringComparison.Ordinal));
        Assert.True(source.Contains("resumeRolloverClaimedResponse", StringComparison.Ordinal));
        Assert.True(source.Contains("rolloverBootstrapTaskId", StringComparison.Ordinal));
        Assert.True(source.Contains("value.pathname=value.pathname.replace", StringComparison.Ordinal));
    }

    [Fact]
    public void BridgeTask_ExposesConversationRolloverState()
    {
        var properties = typeof(BridgeTask)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RolloverPrompt", properties);
        Assert.Contains("RolloverPending", properties);
        Assert.Contains("RolloverReason", properties);
        Assert.Contains("RolloverCount", properties);
    }

    [Fact]
    public void BridgeExpectedExtensionIdentity_MatchesEmbeddedExtension()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var expectedVersion = typeof(BridgeServer)
            .GetField("ExpectedExtensionVersion", flags)?
            .GetRawConstantValue() as string;
        var expectedBuild = typeof(BridgeServer)
            .GetField("ExpectedExtensionBuild", flags)?
            .GetRawConstantValue() as string;

        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));
        Assert.False(string.IsNullOrWhiteSpace(expectedBuild));

        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProjectHub.Worker.Extension.manifest.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var manifestVersion = document.RootElement.GetProperty("version").GetString();

        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");
        var contentVersion = Regex.Match(source, @"const EXTENSION_VERSION = '([^']+)';");
        var contentBuild = Regex.Match(source, @"const EXTENSION_BUILD = '([^']+)';");

        Assert.True(contentVersion.Success);
        Assert.True(contentBuild.Success);
        Assert.Equal(manifestVersion, expectedVersion);
        Assert.Equal(contentVersion.Groups[1].Value, expectedVersion);
        Assert.Equal(contentBuild.Groups[1].Value, expectedBuild);
    }

    [Fact]
    public void Bridge_CreatesManagedRuntimeToken()
    {
        using var bridge = new BridgeServer();

        Assert.False(string.IsNullOrWhiteSpace(bridge.ManagedRuntimeToken));
        Assert.True(bridge.ManagedRuntimeToken.Length >= 32);
    }

    private static string ReadEmbeddedText(string name)
    {
        var assembly = typeof(BridgeServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
