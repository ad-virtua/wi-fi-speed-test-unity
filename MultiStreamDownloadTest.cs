using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; // UIを使用
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ダウンロード中に随時呼ばれるDownloadHandlerScriptの実装。
/// ReceiveDataが呼ばれるたびに受信バイト数をカウントし続ける。
/// </summary>
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

/// <summary>
/// 複数ストリームでのダウンロードテストを行い、
/// グレース期間後から計測期間中のダウンロード速度を測定し、
/// 5秒おきに途中経過（受信バイト数）をログ出力およびUI上に表示する。
/// 
/// リクエスト開始時にhandlersにDownloadHandlerを追加することで、
/// リクエスト完了前から途中経過に反映されるようにしている。
/// </summary>
public class MultiStreamDownloadTest : MonoBehaviour
{
    [Header("Settings")]
    public string serverURL = "https://speedtest-727758005602.asia-northeast1.run.app/garbage.php"; 
    public int ckSize = 100;     // 100MBチャンク
    public float testDuration = 15f;  // 総テスト時間（秒）
    public float graceTime = 1.5f;    // グレース期間（秒）
    public int streamCount = 6;       // 同時ストリーム数
    public float overheadCompensationFactor = 1.06f; // オーバーヘッド補正係数
    public bool useMebibits = false;  // Mebibits表示かMbps表示か

    private bool testRunning = false;
    private float startTime;
    private long totalBytesAtGraceEnd = 0;  
    private List<Coroutine> streamCoroutines = new List<Coroutine>();
    private List<MyDownloadHandler> handlers = new List<MyDownloadHandler>(); 
    private bool graceTimeEnded = false;

    // UI関連
    private Text logText;
    private string logMessages = ""; // UI表示用ログ蓄積

    void Start()
    {
        SetupUI();
        LogMessage("Initiating test coroutine...");
        StartCoroutine(StartDownloadTest());
    }

    void SetupUI()
    {
        // Canvasの生成
        GameObject canvasGO = new GameObject("Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Textオブジェクトの生成
        GameObject textGO = new GameObject("LogText");
        textGO.transform.SetParent(canvasGO.transform, false);
        logText = textGO.AddComponent<Text>();
        logText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        logText.fontSize = 14;
        logText.color = Color.black;
        logText.alignment = TextAnchor.UpperLeft;
        logText.rectTransform.anchorMin = new Vector2(0, 1);
        logText.rectTransform.anchorMax = new Vector2(0, 1);
        logText.rectTransform.pivot = new Vector2(0, 1);
        logText.rectTransform.anchoredPosition = new Vector2(10, -10);
        logText.rectTransform.sizeDelta = new Vector2(600, 600);
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

    IEnumerator StartDownloadTest()
    {
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

        // グレース期間終了時点の合計受信量
        long currentTotal = GetTotalDownloadedBytes();
        totalBytesAtGraceEnd = currentTotal;
        graceTimeEnded = true;

        LogMessage("Grace time ended. Current totalBytes=" + currentTotal + ". Starting measurement period...");

        float measureTime = testDuration - graceTime;
        LogMessage("Measuring speed for next " + measureTime + " seconds...");

        // 5秒おきに途中経過表示
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
        LogMessage("===== TEST COMPLETE =====");
    }

    IEnumerator DownloadStreamCoroutine(int streamIndex)
    {
        LogMessage("Stream " + streamIndex + " started.");
        while (testRunning)
        {
            string testUrl = serverURL + "?ckSize=" + ckSize + "&r=" + Random.value;
            UnityWebRequest uwr = new UnityWebRequest(testUrl, UnityWebRequest.kHttpVerbGET);
            var handler = new MyDownloadHandler();
            uwr.downloadHandler = handler;

            // リクエスト開始前にhandlersに追加
            handlers.Add(handler);

            float reqStart = Time.time;
            yield return uwr.SendWebRequest();
            float reqTime = Time.time - reqStart;

            if (!testRunning)
            {
                LogMessage("Stream " + streamIndex + " stopped (test ended).");
                break;
            }

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                // 既にhandlersに追加済みなのでここでは何もしない
                LogMessage($"Stream {streamIndex} request completed in {reqTime:F2}s, received {handler.GetTotalBytesReceived()} bytes (this request).");
            }
            else
            {
                LogMessage($"Stream {streamIndex} error: {uwr.error} (reqTime={reqTime:F2}s)");
                // エラーのため、このリクエストは中途半端に終了
                // 必要ならhandlersから削除するなどの処理を行う
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
