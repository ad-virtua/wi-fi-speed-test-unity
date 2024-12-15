using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

public class MyDownloadHandler : DownloadHandlerScript
{
    private long totalReceivedBytes = 0;
    public MyDownloadHandler() : base() { }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength <= 0)
        {
            Debug.LogWarning("Received a null/empty buffer");
            return false;
        }
        totalReceivedBytes += dataLength;
        return true;
    }

    public long GetTotalBytesReceived()
    {
        return totalReceivedBytes;
    }
}

public class MultiStreamDownloadTest : MonoBehaviour
{
    [Header("Default Settings (will be overwritten by UI)")]
    public string serverURL = "https://librespeed.a573.net/backend/garbage.php"; 
    public int ckSize = 100;     
    public float testDuration = 15f;  
    public float graceTime = 1.5f;   
    public int streamCount = 6;      
    public float overheadCompensationFactor = 1.06f; 
    public bool useMebibits = false; 

    private bool testRunning = false;
    private float startTime;
    private long totalBytesAtGraceEnd = 0;  
    private List<Coroutine> streamCoroutines = new List<Coroutine>();
    private List<MyDownloadHandler> handlers = new List<MyDownloadHandler>(); 
    private bool graceTimeEnded = false;
    private List<UnityWebRequest> ongoingRequests = new List<UnityWebRequest>();

    // UI関連
    private Text logText;
    private string logMessages = "";
    private InputField urlInputField;
    private InputField ckSizeInputField;
    private Button startButton;
    private Text resultText; // Result表示用Text

    void Start()
    {
        SetupUI();
        LogMessage("Please enter URL and ckSize, then press Start.");
    }

    void SetupUI()
    {
        // EventSystemがない場合生成
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        // Canvas生成
        GameObject canvasGO = new GameObject("Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Log表示用Text
        GameObject textGO = new GameObject("LogText");
        textGO.transform.SetParent(canvasGO.transform, false);
        logText = textGO.AddComponent<Text>();
        logText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        logText.fontSize = 14;
        logText.color = Color.black;
        logText.alignment = TextAnchor.UpperLeft;
        RectTransform textRect = logText.rectTransform;
        textRect.anchorMin = new Vector2(0, 1);
        textRect.anchorMax = new Vector2(0, 1);
        textRect.pivot = new Vector2(0, 1);
        textRect.anchoredPosition = new Vector2(10, -10);
        // 表示領域を拡大
        textRect.sizeDelta = new Vector2(1000, 800);

        // URL入力フィールド
        GameObject urlGO = new GameObject("URLInput");
        urlGO.transform.SetParent(canvasGO.transform, false);
        Image urlBG = urlGO.AddComponent<Image>();
        urlBG.color = Color.white;
        urlInputField = urlGO.AddComponent<InputField>();
        urlInputField.readOnly = false;
        urlInputField.interactable = true;

        RectTransform urlRect = urlInputField.GetComponent<RectTransform>();
        urlRect.anchorMin = new Vector2(0, 0);
        urlRect.anchorMax = new Vector2(0, 0);
        urlRect.pivot = new Vector2(0, 0);
        urlRect.anchoredPosition = new Vector2(10, 10);
        urlRect.sizeDelta = new Vector2(300, 30);

        GameObject urlTextGO = new GameObject("URLText");
        urlTextGO.transform.SetParent(urlGO.transform, false);
        Text urlTextComp = urlTextGO.AddComponent<Text>();
        urlTextComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        urlTextComp.color = Color.black;
        urlTextComp.alignment = TextAnchor.MiddleLeft;
        RectTransform urlTextRect = urlTextComp.rectTransform;
        urlTextRect.sizeDelta = new Vector2(300, 30);
        urlTextRect.anchoredPosition = Vector2.zero;

        GameObject urlPlaceholderGO = new GameObject("URLPlaceholder");
        urlPlaceholderGO.transform.SetParent(urlGO.transform, false);
        Text urlPlaceholder = urlPlaceholderGO.AddComponent<Text>();
        urlPlaceholder.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        urlPlaceholder.color = Color.gray;
        urlPlaceholder.alignment = TextAnchor.MiddleLeft;
        urlPlaceholder.text = "Enter URL...";
        RectTransform urlPlaceholderRect = urlPlaceholder.rectTransform;
        urlPlaceholderRect.sizeDelta = new Vector2(300, 30);
        urlPlaceholderRect.anchoredPosition = Vector2.zero;

        urlInputField.textComponent = urlTextComp;
        urlInputField.placeholder = urlPlaceholder;
        urlInputField.text = serverURL;

        // ckSize入力フィールド
        GameObject ckSizeGO = new GameObject("ckSizeInput");
        ckSizeGO.transform.SetParent(canvasGO.transform, false);
        Image ckSizeBG = ckSizeGO.AddComponent<Image>();
        ckSizeBG.color = Color.white;
        ckSizeInputField = ckSizeGO.AddComponent<InputField>();
        ckSizeInputField.readOnly = false;
        ckSizeInputField.interactable = true;

        RectTransform ckSizeRect = ckSizeInputField.GetComponent<RectTransform>();
        ckSizeRect.anchorMin = new Vector2(0, 0);
        ckSizeRect.anchorMax = new Vector2(0, 0);
        ckSizeRect.pivot = new Vector2(0, 0);
        ckSizeRect.anchoredPosition = new Vector2(10, 50);
        ckSizeRect.sizeDelta = new Vector2(100, 30);

        GameObject ckSizeTextGO = new GameObject("ckSizeText");
        ckSizeTextGO.transform.SetParent(ckSizeGO.transform, false);
        Text ckSizeTextComp = ckSizeTextGO.AddComponent<Text>();
        ckSizeTextComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ckSizeTextComp.color = Color.black;
        ckSizeTextComp.alignment = TextAnchor.MiddleLeft;
        RectTransform ckSizeTextRect = ckSizeTextComp.rectTransform;
        ckSizeTextRect.sizeDelta = new Vector2(100, 30);
        ckSizeTextRect.anchoredPosition = Vector2.zero;

        GameObject ckSizePlaceholderGO = new GameObject("ckSizePlaceholder");
        ckSizePlaceholderGO.transform.SetParent(ckSizeGO.transform, false);
        Text ckSizePlaceholder = ckSizePlaceholderGO.AddComponent<Text>();
        ckSizePlaceholder.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ckSizePlaceholder.color = Color.gray;
        ckSizePlaceholder.alignment = TextAnchor.MiddleLeft;
        ckSizePlaceholder.text = "Enter ckSize...";
        RectTransform ckSizePlaceholderRect = ckSizePlaceholder.rectTransform;
        ckSizePlaceholderRect.sizeDelta = new Vector2(100, 30);
        ckSizePlaceholderRect.anchoredPosition = Vector2.zero;

        ckSizeInputField.textComponent = ckSizeTextComp;
        ckSizeInputField.placeholder = ckSizePlaceholder;
        ckSizeInputField.text = ckSize.ToString();

        // Startボタン
        GameObject btnGO = new GameObject("StartButton");
        btnGO.transform.SetParent(canvasGO.transform, false);
        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = Color.green;
        startButton = btnGO.AddComponent<Button>();

        RectTransform btnRect = startButton.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0, 0);
        btnRect.anchorMax = new Vector2(0, 0);
        btnRect.pivot = new Vector2(0, 0);
        btnRect.anchoredPosition = new Vector2(10, 90);
        btnRect.sizeDelta = new Vector2(100, 30);

        GameObject btnTextGO = new GameObject("BtnText");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        Text btnText = btnTextGO.AddComponent<Text>();
        btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.color = Color.black;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.text = "Start";
        RectTransform btnTextRect = btnText.GetComponent<RectTransform>();
        btnTextRect.sizeDelta = new Vector2(100, 30);
        btnTextRect.anchoredPosition = Vector2.zero;

        startButton.interactable = true;
        startButton.onClick.AddListener(StartTest);

        // Result表示用Text (Startボタンの横)
        GameObject resultGO = new GameObject("ResultText");
        resultGO.transform.SetParent(canvasGO.transform, false);
        resultText = resultGO.AddComponent<Text>();
        resultText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        resultText.color = Color.red;
        resultText.alignment = TextAnchor.MiddleLeft;
        resultText.text = "Result: -";
        RectTransform resultRect = resultText.rectTransform;
        resultRect.anchorMin = new Vector2(0, 0);
        resultRect.anchorMax = new Vector2(0, 0);
        resultRect.pivot = new Vector2(0, 0);
        resultRect.anchoredPosition = new Vector2(120, 90);
        resultRect.sizeDelta = new Vector2(200, 30);
    }

    void LogMessage(string msg)
    {
        Debug.Log(msg);
        logMessages += msg + "\n";
        if (logText != null)
        {
            logText.text = logMessages;
        }
    }

    public void StartTest()
    {
        string urlInput = urlInputField.text;
        if (!string.IsNullOrEmpty(urlInput))
        {
            serverURL = urlInput;
        }

        string ckSizeStr = ckSizeInputField.text;
        int parsedCk;
        if (int.TryParse(ckSizeStr, out parsedCk))
        {
            ckSize = parsedCk;
        }

        LogMessage("Starting test with URL=" + serverURL + ", ckSize=" + ckSize);
        StartCoroutine(StartDownloadTestCoroutine());
    }

    IEnumerator StartDownloadTestCoroutine()
    {
        handlers.Clear();
        ongoingRequests.Clear();
        streamCoroutines.Clear();
        testRunning = false;
        graceTimeEnded = false;

        logMessages = ""; 
        LogMessage("===== STARTING DOWNLOAD TEST =====");
        LogMessage($"Settings: serverURL={serverURL}, ckSize={ckSize}, testDuration={testDuration}s, graceTime={graceTime}s, " +
                  $"streamCount={streamCount}, overheadFactor={overheadCompensationFactor}, useMebibits={useMebibits}");

        testRunning = true;
        startTime = Time.time;
        graceTimeEnded = false;

        LogMessage("Starting " + streamCount + " parallel streams...");
        for (int i = 0; i < streamCount; i++)
        {
            var c = StartCoroutine(DownloadStreamCoroutine(i));
            streamCoroutines.Add(c);
        }

        LogMessage("Waiting for grace time (" + graceTime + "s)...");
        yield return new WaitForSeconds(graceTime);

        if (!testRunning)
        {
            LogMessage("Test aborted before grace time ended.");
            yield break;
        }

        long currentTotal = GetTotalDownloadedBytes();
        totalBytesAtGraceEnd = currentTotal;
        graceTimeEnded = true;

        LogMessage("Grace time ended. Current totalBytes=" + currentTotal + ". Starting measurement period...");

        float measureTime = testDuration - graceTime;
        LogMessage("Measuring speed for next " + measureTime + " seconds...");

        float endTime = Time.time + measureTime;
        while (Time.time < endTime && testRunning)
        {
            yield return new WaitForSeconds(5f);
            long currentMeasured = GetTotalDownloadedBytes() - totalBytesAtGraceEnd;
            LogMessage($"[Progress Update] Elapsed: {(Time.time - (startTime + graceTime)):F2}s, Downloaded: {currentMeasured} bytes during measurement");
        }

        LogMessage("Measurement period ended, stopping streams...");
        StopAllStreams();
        testRunning = false;
        AbortAllOngoingRequests();
        LogMessage("testRunning set to false.");

        long finalTotal = GetTotalDownloadedBytes();
        long actualMeasuredBytes = finalTotal - totalBytesAtGraceEnd;

        float elapsed = testDuration - graceTime; 
        float divisor = useMebibits ? 1048576f : 1000000f;
        float speedMbps = ((actualMeasuredBytes * 8f * overheadCompensationFactor) / (elapsed * divisor));

        LogMessage("Download test finished");
        LogMessage("Elapsed (measurement only): " + elapsed.ToString("F2") + "s, "
                  + "Downloaded: " + actualMeasuredBytes + " bytes during measurement period");
        LogMessage("Download Speed: " + speedMbps.ToString("F2") + (useMebibits ? " Mebibits/s" : " Mbps"));

        // 結果をResultTextに反映
        if (resultText != null)
        {
            resultText.text = "Result: " + speedMbps.ToString("F2") + (useMebibits ? " Mebibits/s" : " Mbps");
        }

        LogMessage("===== TEST COMPLETE =====");
    }

    IEnumerator DownloadStreamCoroutine(int streamIndex)
    {
        LogMessage("Stream " + streamIndex + " started.");
        while (testRunning)
        {
            string testUrl = serverURL + "?ckSize=" + ckSize + "&r=" + Random.value + "&cors=1";
            UnityWebRequest uwr = new UnityWebRequest(testUrl, UnityWebRequest.kHttpVerbGET);
            var handler = new MyDownloadHandler();
            uwr.downloadHandler = handler;

            handlers.Add(handler);
            ongoingRequests.Add(uwr);

            float reqStart = Time.time;
            yield return uwr.SendWebRequest();
            ongoingRequests.Remove(uwr);

            float reqTime = Time.time - reqStart;

            if (!testRunning)
            {
                LogMessage("Stream " + streamIndex + " stopped (test ended).");
                break;
            }

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                LogMessage($"Stream {streamIndex} request completed in {reqTime:F2}s, received {handler.GetTotalBytesReceived()} bytes (this request).");
            }
            else
            {
                LogMessage($"Stream {streamIndex} error: {uwr.error} (reqTime={reqTime:F2}s)");
                yield return new WaitForSeconds(0.5f);
            }
        }
        LogMessage("Stream " + streamIndex + " coroutine ended.");
    }

    void StopAllStreams()
    {
        LogMessage("Stopping all streams...");
        foreach (var c in streamCoroutines)
        {
            if (c != null)
            {
                StopCoroutine(c);
            }
        }
        streamCoroutines.Clear();
        LogMessage("All streams stopped.");
    }

    void AbortAllOngoingRequests()
    {
        LogMessage("Aborting all ongoing requests...");
        foreach (var req in ongoingRequests)
        {
            if (!req.isDone)
            {
                req.Abort();
            }
        }
        ongoingRequests.Clear();
        LogMessage("All ongoing requests aborted.");
    }

    long GetTotalDownloadedBytes()
    {
        long sum = 0;
        foreach (var h in handlers)
        {
            sum += h.GetTotalBytesReceived();
        }
        return sum;
    }
}
