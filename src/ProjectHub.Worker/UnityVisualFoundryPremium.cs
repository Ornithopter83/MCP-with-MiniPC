using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Adds deterministic art direction, shader/material depth, composition
/// alternatives and multi-track motion to the isolated Unity prefabs.
/// No model, package installation, network or GUI foreground access.
/// </summary>
internal static class UnityVisualFoundryPremium
{
    private const string ShaderSource = """
        Shader "ProjectHub/VisualFoundry/PrismaticUI"
        {
            Properties
            {
                [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
                _Color ("Tint", Color) = (1,1,1,1)
                _Accent ("Accent", Color) = (0.1,0.8,1,1)
                _Glow ("Emission", Range(0,1)) = 0.28
                _Shimmer ("Shimmer", Range(0,1)) = 0.3
                _StencilComp ("Stencil Comparison", Float) = 8
                _Stencil ("Stencil ID", Float) = 0
                _StencilOp ("Stencil Operation", Float) = 0
                _StencilWriteMask ("Stencil Write Mask", Float) = 255
                _StencilReadMask ("Stencil Read Mask", Float) = 255
                _ColorMask ("Color Mask", Float) = 15
            }
            SubShader
            {
                Tags { "Queue"="Transparent" "IgnoreProjector"="True"
                    "RenderType"="Transparent" "PreviewType"="Plane"
                    "CanUseSpriteAtlas"="True" }
                Stencil
                {
                    Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
                    ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
                }
                Cull Off Lighting Off ZWrite Off
                ZTest [unity_GUIZTestMode]
                Blend SrcAlpha OneMinusSrcAlpha
                ColorMask [_ColorMask]
                Pass
                {
                    CGPROGRAM
                    #pragma vertex vert
                    #pragma fragment frag
                    #include "UnityCG.cginc"
                    struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
                    struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
                    sampler2D _MainTex; fixed4 _Color; fixed4 _Accent;
                    float _Glow; float _Shimmer; fixed4 _TextureSampleAdd;
                    v2f vert(appdata v)
                    {
                        v2f o; o.vertex = UnityObjectToClipPos(v.vertex);
                        o.uv = v.uv; o.color = v.color * _Color; return o;
                    }
                    fixed4 frag(v2f i) : SV_Target
                    {
                        fixed4 baseColor = (tex2D(_MainTex,i.uv) + _TextureSampleAdd) * i.color;
                        float2 e = min(i.uv, 1.0 - i.uv);
                        float border = 1.0 - smoothstep(0.0,0.018,min(e.x,e.y));
                        float streak = 1.0 - smoothstep(0.0,0.085,
                            abs(frac(i.uv.x * 0.72 - i.uv.y * 0.12 - _Time.y * 0.07) - 0.5));
                        float glimmer = (streak * _Shimmer * 0.24 + border * _Glow);
                        baseColor.rgb = lerp(baseColor.rgb, _Accent.rgb, saturate(glimmer));
                        return baseColor;
                    }
                    ENDCG
                }
            }
        }
        """;

    private const string CreateMaterial = """
            // Premium procedural material is stored beside the generated kit.
            // Shader import failure should not damage existing application UI.
            var vfShaderPath = vfOutput + "/PrismaticUI.shader";
            var vfShaderFile = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                vfShaderPath.Substring("Assets/".Length).Replace('/',
                    System.IO.Path.DirectorySeparatorChar));
            System.IO.File.WriteAllText(vfShaderFile, __SHADER_CONTENT__);
            UnityEditor.AssetDatabase.ImportAsset(vfShaderPath,
                UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            var vfShader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(
                vfShaderPath);
            UnityEngine.Material vfPremiumMat = null;
            if (vfShader != null && vfShader.isSupported) {
                vfPremiumMat = new UnityEngine.Material(vfShader);
                vfPremiumMat.SetColor("_Accent", vfColor(vfAccent));
                vfPremiumMat.SetFloat("_Glow", .60f);
                vfPremiumMat.SetFloat("_Shimmer", .6f);
                UnityEditor.AssetDatabase.CreateAsset(vfPremiumMat,
                    vfOutput + "/PrismaticUI.mat");
            }
            // Animated atmospheric halo is baked as a Sprite. This is
            // pipeline-neutral and requires no external image-generation model.
            var vfHaloTexture = new UnityEngine.Texture2D(256,256,
                UnityEngine.TextureFormat.RGBA32,false);
            var vfHaloPixels = new UnityEngine.Color[256*256];
            for(int vfY=0;vfY<256;vfY++)
                for(int vfX=0;vfX<256;vfX++) {
                    var vfDx=(vfX-127.5f)/127.5f;
                    var vfDy=(vfY-127.5f)/127.5f;
                    var vfRadius = UnityEngine.Mathf.Sqrt(vfDx*vfDx + vfDy*vfDy);
                    var vfGlow = UnityEngine.Mathf.Pow(
                        UnityEngine.Mathf.Clamp01(1f-vfRadius),2.8f);
                    var vfHue = UnityEngine.Color.Lerp(vfColor(vfSecondary),
                        vfColor(vfAccent),.6f+vfDx*.2f);
                    vfHue.a = vfGlow*.73f;
                    vfHaloPixels[vfY*256+vfX]=vfHue;
                }
            vfHaloTexture.SetPixels(vfHaloPixels);
            vfHaloTexture.Apply();
            var vfHaloPath = vfOutput+"/Halo.png";
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    vfHaloPath.Substring("Assets/".Length).Replace('/',
                        System.IO.Path.DirectorySeparatorChar)),
                vfHaloTexture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(vfHaloTexture);
            UnityEditor.AssetDatabase.ImportAsset(vfHaloPath,
                UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            var vfHaloImporter = UnityEditor.AssetImporter.GetAtPath(vfHaloPath)
                as UnityEditor.TextureImporter;
            if(vfHaloImporter == null)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_HALO_IMPORT_FAILED");
            vfHaloImporter.textureType = UnityEditor.TextureImporterType.Sprite;
            vfHaloImporter.mipmapEnabled = false;
            vfHaloImporter.SaveAndReimport();
            var vfHaloSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                vfHaloPath);
            if(vfHaloSprite == null)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_HALO_SPRITE_MISSING");
            """;

    private const string AddDecor = """
                // Scene-level production details are deterministic art assets:
                // multiple independent layers, corner geometry, a low-density
                // directional grid and a focal highlight. No AI prompt needed.
                var vfSweep = vfPlate("Atmospheric rim",vfRoot.transform,
                    new UnityEngine.Vector2(0,393),
                    new UnityEngine.Vector2(1480,2),vfColor(vfSecondary) * .38f);
                var vfHalo = vfPlate("Atmospheric halo",vfRoot.transform,
                    new UnityEngine.Vector2(699,322),
                    new UnityEngine.Vector2(410,410),UnityEngine.Color.white);
                vfHalo.GetComponent<UnityEngine.UI.Image>().sprite = vfHaloSprite;
                var vfSignal = vfPlate("Status indicator",vfRoot.transform,
                    new UnityEngine.Vector2(782,439),
                    new UnityEngine.Vector2(36,36),vfColor(vfAccent));
                if (vfPremiumMat != null)
                    vfSignal.GetComponent<UnityEngine.UI.Image>().material = vfPremiumMat;
                for(int vfTick=0; vfTick<10; vfTick++) {
                    var vfTag = vfPlate("Peripheral graduation " + vfTick,
                        vfRoot.transform,new UnityEngine.Vector2(-867,330 - vfTick*72),
                        new UnityEngine.Vector2(vfTick%3==0 ? 30:13,2),
                        vfColor(vfAccent) * .65f);
                }
                for(int vfIndex=0; vfIndex<7; vfIndex++) {
                    var vfTrack = vfPlate("Architectural grid " + vfIndex,
                        vfRoot.transform,
                        new UnityEngine.Vector2(-680 + vfIndex*225,-435),
                        new UnityEngine.Vector2(1,20),vfColor(vfMuted) * .20f);
                }
                var vfHero = vfPlate("Editorial kicker",vfRoot.transform,
                    new UnityEngine.Vector2(0,365),
                    new UnityEngine.Vector2(940,36),
                    new UnityEngine.Color(0f,0f,0f,0f));
                vfLabel(vfHero,vfScreen == "Quiz" ? "LIVE INTELLIGENCE / 001" :
                    vfScreen == "Menu" ? "WELCOME TO THE NEXT FRONTIER" :
                    "MISSION DATA // DEBRIEF",17,
                    UnityEngine.TextAnchor.MiddleCenter,vfColor(vfSecondary));
                """;

    private const string EnhanceChoices = """
                    var vfHighlight = vfPlate("Focus line",buttonPlate,
                        new UnityEngine.Vector2(0,43),
                        new UnityEngine.Vector2(1030,1),
                        vfColor(vfAccent) * (i==0 ? .75f : .20f));
                    if (vfPremiumMat != null) {
                        buttonPlate.GetComponent<UnityEngine.UI.Image>().material = vfPremiumMat;
                    }
                    var shadow = buttonPlate.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                    shadow.effectColor = new UnityEngine.Color(0,0,0,.42f);
                    shadow.effectDistance = new UnityEngine.Vector2(0,-8);
                    """;

    private const string VariantPrefabs = """
                // The base prefab is always the stable integration target.
                // Two alternatives provide art-directed composition choices
                // without a second AI invocation or mutating the user's scene.
                for (int vfVariant=1; vfVariant<=2; vfVariant++) {
                    var vfAlternative = UnityEngine.Object.Instantiate(vfRoot);
                    vfAlternative.name += vfVariant==1 ? " - Editorial" : " - Immersive";
                    var vfAltFrame = vfAlternative.transform.Find("Main glass body")
                        .GetComponent<UnityEngine.RectTransform>();
                    var vfAltTitle = vfAlternative.transform.Find("Heading")
                        .GetComponent<UnityEngine.RectTransform>();
                    var vfAltLead = vfAlternative.transform.Find("Content lead")
                        .GetComponent<UnityEngine.RectTransform>();
                    var vfAltSecondary = vfAlternative.transform.Find("Secondary heading")
                        .GetComponent<UnityEngine.RectTransform>();
                    if(vfVariant==1) {
                        vfAltFrame.sizeDelta = new UnityEngine.Vector2(1550,800);
                        vfAltTitle.anchoredPosition = new UnityEngine.Vector2(-145,300);
                        vfAltTitle.sizeDelta = new UnityEngine.Vector2(1100,110);
                        vfAltLead.anchoredPosition = new UnityEngine.Vector2(-80,122);
                        vfAltSecondary.anchoredPosition = new UnityEngine.Vector2(-110,219);
                    } else {
                        vfAltFrame.sizeDelta = new UnityEngine.Vector2(1610,850);
                        vfAltTitle.anchoredPosition = new UnityEngine.Vector2(0,314);
                        vfAltTitle.sizeDelta = new UnityEngine.Vector2(1350,125);
                        vfAltLead.anchoredPosition = new UnityEngine.Vector2(0,112);
                        vfAltSecondary.anchoredPosition = new UnityEngine.Vector2(0,233);
                    }
                    for(int vfChoice=0;vfChoice<4;vfChoice++) {
                        var vfAltButton = vfAlternative.transform.Find("Action " + (vfChoice+1))
                            .GetComponent<UnityEngine.RectTransform>();
                        vfAltButton.sizeDelta = vfVariant==1
                            ? new UnityEngine.Vector2(1020,88)
                            : new UnityEngine.Vector2(1210,82);
                        vfAltButton.anchoredPosition = vfVariant==1
                            ? new UnityEngine.Vector2(160,-1 - vfChoice * 108)
                            : new UnityEngine.Vector2(0,-15 - vfChoice * 96);
                    }
                    var vfVariantPath = vfOutput + "/" + vfScreen +
                        (vfVariant == 1 ? "-Editorial" : "-Immersive") + ".prefab";
                    UnityEditor.PrefabUtility.SaveAsPrefabAsset(vfAlternative,vfVariantPath);
                    UnityEngine.Object.DestroyImmediate(vfAlternative);
                    vfPrefabPaths.Add(vfVariantPath);
                }
                """;

    private const string ExtraMotion = """
                // Multi-track entrance: opacity + gentle cinematic settling,
                // with an independent breathing accent loop on foreground.
                clip.SetCurve("",typeof(UnityEngine.Transform),"m_LocalScale.x",
                    UnityEngine.AnimationCurve.EaseInOut(0f,.975f,.7f,1f));
                clip.SetCurve("",typeof(UnityEngine.Transform),"m_LocalScale.y",
                    UnityEngine.AnimationCurve.EaseInOut(0f,.975f,.7f,1f));
                var vfPulse = new UnityEngine.AnimationClip();
                vfPulse.legacy = true; vfPulse.frameRate = 60;
                vfPulse.wrapMode = UnityEngine.WrapMode.Loop;
                var vfPulseCurve = new UnityEngine.AnimationCurve(
                    new UnityEngine.Keyframe(0f,.35f),
                    new UnityEngine.Keyframe(1.5f,.96f),
                    new UnityEngine.Keyframe(3f,.35f));
                vfPulseCurve.preWrapMode = UnityEngine.WrapMode.Loop;
                vfPulseCurve.postWrapMode = UnityEngine.WrapMode.Loop;
                vfPulse.SetCurve("",typeof(UnityEngine.UI.Image),"m_Color.a",vfPulseCurve);
                UnityEditor.AssetDatabase.CreateAsset(vfPulse,
                    vfOutput + "/" + vfScreen + "-pulse.anim");
                var vfSignalAnimation = vfSignal.gameObject.AddComponent<UnityEngine.Animation>();
                vfSignalAnimation.AddClip(vfPulse,"Pulse");
                vfSignalAnimation.clip = vfPulse;
                vfSignalAnimation.playAutomatically = true;
                """;

    private const string LayoutAudit = """
            // Asset-side deterministic layout audit. Does not infer beauty
            // or certify a native Game View screenshot.
            var vfAudit = new System.Text.StringBuilder();
            vfAudit.Append("{\"generator\":\"VisualFoundry\",\"style\":\"")
                .Append(vfStyle).Append("\",\"screens\":3,\"variants\":3,")
                .Append("\"prefabs\":").Append(vfPrefabPaths.Count)
                .Append(",\"targetWidth\":1920,\"targetHeight\":1080,")
                .Append("\"inspected\":false,\"binding\":\"required\"}");
            var vfAuditPath = vfOutput + "/LayoutAudit.json";
            var vfAuditFile = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                vfAuditPath.Substring("Assets/".Length).Replace('/',
                    System.IO.Path.DirectorySeparatorChar));
            System.IO.File.WriteAllText(vfAuditFile,vfAudit.ToString());
            UnityEditor.AssetDatabase.ImportAsset(vfAuditPath,
                UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            """;

    internal static string Upgrade(string source)
    {
        var shader = JsonSerializer.Serialize(ShaderSource);
        return ReplaceOnce(
            ReplaceOnce(
                ReplaceOnce(
                    ReplaceOnce(
                        ReplaceOnce(
                            ReplaceOnce(source,
                                "            var vfScreens = new [] {",
                                CreateMaterial.Replace("__SHADER_CONTENT__", shader,
                                    StringComparison.Ordinal) +
                                "            var vfScreens = new [] {"),
                            "                var frame = vfPlate(",
                            AddDecor + "                var frame = vfPlate("),
                        "                    vfLabel(buttonPlate,vfChoices[i]",
                        EnhanceChoices + "                    vfLabel(buttonPlate,vfChoices[i]"),
                    "                UnityEditor.AssetDatabase.CreateAsset(clip,clipPath);",
                    ExtraMotion +
                    "                UnityEditor.AssetDatabase.CreateAsset(clip,clipPath);"),
                "                UnityEngine.Object.DestroyImmediate(vfRoot);",
                VariantPrefabs +
                "                UnityEngine.Object.DestroyImmediate(vfRoot);"),
            "            UnityEditor.AssetDatabase.SaveAssets();",
            LayoutAudit + "            UnityEditor.AssetDatabase.SaveAssets();");
    }

    private static string ReplaceOnce(string s, string find, string replace)
    {
        var at = s.IndexOf(find, StringComparison.Ordinal);
        if (at < 0 || s.IndexOf(find, at + find.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("VISUAL_FOUNDRY_PREMIUM_TEMPLATE_INVALID");
        return s.Replace(find, replace, StringComparison.Ordinal);
    }
}
