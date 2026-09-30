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

        Assert.Equal("0.4.2", document.RootElement.GetProperty("version").GetString());
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
    public void EmbeddedContent_UsesCorrelationKeyToEscapeSendConfirmWithoutTurnSelectors()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("directCorrelationResponseRoot", StringComparison.Ordinal));
        Assert.True(source.Contains("correlationKeyOccurrenceCount", StringComparison.Ordinal));
        Assert.True(source.Contains("document.body.querySelectorAll(selectors)", StringComparison.Ordinal));
        Assert.True(source.Contains("CORRELATION_KEY_BODY", StringComparison.Ordinal));
        Assert.True(source.Contains("keyOccurrences=", StringComparison.Ordinal));
        Assert.True(source.Contains("correlationSendEvidence", StringComparison.Ordinal));
        Assert.True(source.Contains("CORRELATION_KEY_RESPONSE", StringComparison.Ordinal));
        Assert.True(source.Contains("CORRELATION_KEY", StringComparison.Ordinal));
        Assert.True(source.Contains("correlatedResponseSlice", StringComparison.Ordinal));
        Assert.True(source.Contains("bestLength", StringComparison.Ordinal));
        Assert.True(source.Contains("values.sort((a,b)=>b.length-a.length)", StringComparison.Ordinal));
        Assert.True(source.Contains("observeCorrelationKeyWatch", StringComparison.Ordinal));
        Assert.True(source.Contains("HQ_KEY_WATCH", StringComparison.Ordinal));
        Assert.True(source.Contains("HQ_KEY_SEND_RECOVERED", StringComparison.Ordinal));
        Assert.True(source.Contains("HQ_KEY_CLAIM_RECOVERED", StringComparison.Ordinal));
        Assert.True(source.Contains("HQ_KEY_BASELINE_FALLBACK", StringComparison.Ordinal));
        Assert.True(source.Contains("const bodyFallback=correlationBodyTextFallback();", StringComparison.Ordinal));
        Assert.True(source.Contains("const correlatedText=currentCorrelatedResponseText();", StringComparison.Ordinal));
        Assert.True(source.Contains("correlationBodyTextFallback", StringComparison.Ordinal));
        Assert.True(source.Contains("keyBodyFallback", StringComparison.Ordinal));
        Assert.True(source.Contains("role·turn selector와 응답 root 없이 현재 KEY와 ACTION이 포함된 body fallback", StringComparison.Ordinal));
        Assert.True(source.Contains("keyBodyFallback=", StringComparison.Ordinal));
        Assert.True(source.Contains("if(activeCorrelationKey)observeCorrelationKeyWatch();", StringComparison.Ordinal));
        Assert.True(source.Contains("enterWaitResponse('기존 turn baseline 없이 현재 HQ KEY를 독립 감시합니다.')", StringComparison.Ordinal));
        Assert.True(source.Contains("activeCorrelationKey=null;activeResource=null", StringComparison.Ordinal));

        var functionIndex = source.IndexOf(
            "function sendConfirmationEvidence(prompt)",
            StringComparison.Ordinal);
        var keyIndex = source.IndexOf(
            "if(activeCorrelationKey)evidence=correlationSendEvidence();",
            functionIndex,
            StringComparison.Ordinal);
        var userTurnIndex = source.IndexOf(
            "hasNewUserMessage(prompt,baselineUserMessages)",
            functionIndex,
            StringComparison.Ordinal);

        Assert.True(functionIndex >= 0);
        Assert.True(keyIndex > functionIndex);
        Assert.True(userTurnIndex > keyIndex);

        var fallbackFunctionIndex = source.IndexOf(
            "function correlationBodyTextFallback()",
            StringComparison.Ordinal);
        var actionGuardIndex = source.IndexOf(
            "[ACTION=(?:CONTINUE|PAUSE|END)",
            fallbackFunctionIndex,
            StringComparison.Ordinal);
        var promptGuardIndex = source.IndexOf(
            "normalizeText(body).includes(probe)",
            fallbackFunctionIndex,
            StringComparison.Ordinal);
        var currentTextIndex = source.IndexOf(
            "function currentCorrelatedResponseText()",
            StringComparison.Ordinal);
        var bodyCandidateIndex = source.IndexOf(
            "add(correlationBodyTextFallback());",
            currentTextIndex,
            StringComparison.Ordinal);
        var longestCandidateIndex = source.IndexOf(
            "values.sort((a,b)=>b.length-a.length)",
            currentTextIndex,
            StringComparison.Ordinal);
        var unconditionalBodyFallbackIndex = source.IndexOf(
            "const keyBodyFallback=activeCorrelationKey?correlationBodyTextFallback():'';",
            StringComparison.Ordinal);

        Assert.True(fallbackFunctionIndex >= 0);
        Assert.True(actionGuardIndex > fallbackFunctionIndex);
        Assert.True(promptGuardIndex > actionGuardIndex);
        Assert.True(bodyCandidateIndex > currentTextIndex);
        Assert.True(longestCandidateIndex > bodyCandidateIndex);
        Assert.True(unconditionalBodyFallbackIndex > currentTextIndex);
    }

    [Fact]
    public void EmbeddedContent_DoesNotFreezeCorrelationOnFirstMutationFragment()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        var rootFunction = source.IndexOf(
            "function correlationResponseRoot()",
            StringComparison.Ordinal);
        var mutationCandidate = source.IndexOf(
            "if(mutationResponseElement?.isConnected)candidates.push(mutationResponseElement);",
            rootFunction,
            StringComparison.Ordinal);
        var bestLength = source.IndexOf(
            "let best=null,bestLength=-1,markerOnly=null;",
            rootFunction,
            StringComparison.Ordinal);
        var currentText = source.IndexOf(
            "function currentCorrelatedResponseText()",
            StringComparison.Ordinal);
        var bodyFallback = source.IndexOf(
            "add(correlationBodyTextFallback());",
            currentText,
            StringComparison.Ordinal);
        var longest = source.IndexOf(
            "values.sort((a,b)=>b.length-a.length)",
            currentText,
            StringComparison.Ordinal);

        Assert.True(rootFunction >= 0);
        Assert.True(mutationCandidate > rootFunction);
        Assert.True(bestLength > mutationCandidate);
        Assert.True(currentText > rootFunction);
        Assert.True(bodyFallback > currentText);
        Assert.True(longest > bodyFallback);
        Assert.False(source.Contains(
            "for(const candidate of candidates){\n      const root=correlationRootFromElement(candidate);\n      if(root)return root;",
            StringComparison.Ordinal));
        Assert.False(source.Contains(
            "const keyBodyFallback=activeCorrelationKey&&!keyResponse?correlationBodyTextFallback():'';",
            StringComparison.Ordinal));
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
        Assert.True(source.Contains("resourcePayloadsWithRetry", StringComparison.Ordinal));
        Assert.True(source.Contains("RESOURCE_CAPTURE_RETRY", StringComparison.Ordinal));
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
        Assert.True(source.Contains("hasMeaningfulResponseText", StringComparison.Ordinal));
        Assert.True(source.Contains("scheduleResponseRecheck", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_DEADLINE_RECOVERY", StringComparison.Ordinal));
        Assert.True(source.Contains("POST_STREAM_CHECK", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_LOST_AFTER_STREAM_END", StringComparison.Ordinal));
        Assert.True(source.Contains("response_lost_after_stream_end", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_KEY_MISSING", StringComparison.Ordinal));
        Assert.True(source.Contains("response_key_missing", StringComparison.Ordinal));
        Assert.True(source.Contains("RESPONSE_BODY_MISSING_AFTER_STREAM_END", StringComparison.Ordinal));
        Assert.False(source.Contains("button,[role=\"button\"],[data-is-streaming=\"true\"],[data-streaming=\"true\"],[aria-busy=\"true\"]", StringComparison.Ordinal));
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
    public void EmbeddedContent_ObservesChatUiAnomalyWithoutIntervention()
    {
        var source = ReadEmbeddedText("ProjectHub.Worker.Extension.content.js");

        Assert.True(source.Contains("pageUiAnomaly", StringComparison.Ordinal));
        Assert.True(source.Contains("observePassiveUiAnomaly", StringComparison.Ordinal));
        Assert.True(source.Contains("WEB_UI_ANOMALY_OBSERVED", StringComparison.Ordinal));
        Assert.True(source.Contains("stream recovery polling timed out", StringComparison.Ordinal));
        Assert.True(source.Contains("lastUiAnomalyFingerprint", StringComparison.Ordinal));

        Assert.False(source.Contains("CONVERSATION_ROLLOVER_TRIGGER", StringComparison.Ordinal));
        Assert.False(source.Contains("requestConversationRollover", StringComparison.Ordinal));
        Assert.False(source.Contains("bootstrapRolloverTask", StringComparison.Ordinal));
        Assert.False(source.Contains("/rollover/attach", StringComparison.Ordinal));
        Assert.False(source.Contains("location.assign(newConversationUrl())", StringComparison.Ordinal));
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
