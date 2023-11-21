using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlantUMLLiveViewer {
    public partial class App : Application {
        public const string Name = "PlantUMLLiveViewer";

        // PlantUMLFile 파일을 받을 하위 폴더 이름
        private const string PlantUMLFolder = "plantuml";
        // 받은 PlantUML jar 파일 이름
        private const string PlantUMLFile = "plantuml.jar";
        // 다운 받을 PlantUML jar 파일 URL
        private const string PlantUMLDownloadURL = "https://github.com/plantuml/plantuml/releases/download/v1.2023.12/plantuml-1.2023.12.jar";
        // .puml 파일 인코딩
        private const string inputCharset = "-charset utf8";
        // 저장 기능으로 사용할 출력 파일 형식
        private const string saveFormat = "-tpng";

        private static string plantUMLPath;
        private static string sourcePath;
        private static string tmpFolder;
        private static string tmpFilePath;
        private static string tmpFileStdOut;
        private static string tmpFileStdErr;
        private static FileSystemWatcher sourceWatcher;
        private static int changedValue = 0;

        public static App Inst { get { return Current as App; } }

        public static MainWindow MainWnd { get { return Inst.MainWindow as MainWindow; } }

        private async void InitApp() {
            SetStatusMsg("어플리케이션 초기화 중...");
            var args = Environment.GetCommandLineArgs();
            if (args == null || args.Length != 2) {
                Usage(".puml 파일을 지정해야 합니다.");
                return;
            }

            sourcePath = args[1];
            if (!File.Exists(sourcePath)) {
                ErrorMsg(".puml 파일이 존재하지 않습니다.");
                return;
            }

            MainWindow.Title = $"{Path.GetFileName(sourcePath)} - {Name}";

            SetStatusMsg("Java 실행 파일 찾는 중...");
            if (!FindJavaExec()) {
                ErrorMsg("Java 실행 파일을 찾을 수 없습니다.");
                return;
            }

            SetStatusMsg("PlantUML 실행 파일 찾는 중...");
            plantUMLPath = FindPlantUML(args[0]);
            if (plantUMLPath == null)
                plantUMLPath = await DownloadPlantUML(args[0]);
            if (plantUMLPath == null) {
                ErrorMsg("PlantUML 실행 파일을 다운로드 할 수 없습니다");
                return;
            }

            Current.Exit += OnExited;

            SetStatusMsg("임시 파일/폴더 준비 중...");
            try {
#if DEBUG
                var newTmpFolder = Path.GetFullPath(".\\Temp");
#else
                var userTemp = Path.GetTempPath();
                var newTmpFolder = Path.GetFullPath(Path.Combine(userTemp, Path.GetRandomFileName()));
#endif
                Directory.CreateDirectory(newTmpFolder);
                tmpFolder = newTmpFolder;
                tmpFilePath = Path.GetFullPath(Path.Combine(tmpFolder, Path.GetFileNameWithoutExtension(sourcePath) + ".png"));

                tmpFileStdOut = Path.GetTempFileName();
                tmpFileStdErr = Path.GetTempFileName();
            }
            catch (Exception ex) {
                ErrorMsg($"임시 파일/폴더 생성 실패\n\n{ex.Message}");
                return;
            }

            sourceWatcher = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(sourcePath)), Path.GetFileName(sourcePath)) {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            sourceWatcher.Changed += (s, e) => {
                if (Interlocked.Increment(ref changedValue) == 1)
                    Task.Run(RegenerateLiveImage);
            };
            sourceWatcher.EnableRaisingEvents = true;

            SetStatusMsg("PlantUML 실행 파일 확인 중...");
            var (success, errmsg) = await RunPlanetUML("-version");
            SetStatusMsg(errmsg, success);

            SetStatusMsg("생성 시작...");
            success = await GenerateLiveImage();
            SetStatusMsg("생성 완료", success);
            if (success)
                MainWnd?.RefreshLiveImage(tmpFilePath);
        }

        public async void Save() {
            SetStatusMsg("현재 내용 저장 중...");
            var (success, msg) = await RunPlanetUML($"{saveFormat} {inputCharset} \"{sourcePath}\"");
            SetStatusMsg(msg, success);
        }

        public string GetLastStdOut() {
            try {
                return File.ReadAllText(tmpFileStdOut);
            }
            catch {
            }
            return "";
        }

        public string GetLastStdErr() {
            try {
                return File.ReadAllText(tmpFileStdErr);
            }
            catch {
            }
            return "";
        }

        public void SetStatusMsg(string msg) { Current.Dispatcher.Invoke(() => { MainWnd?.SetStatusMsg(msg); }); }
        public void SetStatusMsg(string msg, bool success) { Current.Dispatcher.Invoke(() => { MainWnd?.SetStatusMsg(msg, success); }); }

        private void Usage(string message) {
            MessageBox.Show(
                $"{message}"
                + "\n"
                + "Usage: PlantUMLLiveViewer.exe <.puml file>\n"
                + "\n"
                + "Copyright BS Since 2022, License GPLv3"
                + "https://github.com/bspfp/PlantUMLLiveViewer \n"
                + "\n"
                + "Image resources:\n"
                + "\n"
                + "[Width icons created by Freepik - Flaticon](https://www.flaticon.com/free-icons/width)\n"
                + "[Resize icons created by Freepik - Flaticon](https://www.flaticon.com/free-icons/resize)\n"
                + "[Power icons created by Uniconlabs - Flaticon](https://www.flaticon.com/free-icons/power)\n"
                + "[Zoom in icons created by Freepik - Flaticon](https://www.flaticon.com/free-icons/zoom-in)\n"
                + "[Save icons created by Freepik - Flaticon](https://www.flaticon.com/free-icons/save)\n"
                + "[illustrated by 머쉬룸](https://blog.naver.com/lastmistake/221002125678)\n"
                ,
                Name, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
        }

        private void ErrorMsg(string message) {
            MessageBox.Show(message, Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }

        #region 이벤트 핸들러
        private void OnExited(object sender, EventArgs e) {
            try {
                if (tmpFolder != null)
                    Directory.Delete(tmpFolder, true);
            }
            catch { }
            tmpFolder = null;

            try {
                if (tmpFileStdOut != null)
                    File.Delete(tmpFileStdOut);
            }
            catch { }
            tmpFileStdOut = null;

            try {
                if (tmpFileStdErr != null)
                    File.Delete(tmpFileStdErr);
            }
            catch { }
            tmpFileStdErr = null;
            try {
                sourceWatcher?.Dispose();
                sourceWatcher = null;
            }
            catch { }
        }

        public void OnLoadedMainWindow() {
            InitApp();
        }
        #endregion

        #region 파일 관리
        private string FindPlantUML(string execPath) {
            try {
                var findTarget = $"{PlantUMLFolder}/{PlantUMLFile}";
                var execFolder = Path.GetDirectoryName(execPath);
                var folder = execFolder;
                while (folder != null) {
                    var jarPath = Path.Combine(folder, findTarget);
                    if (File.Exists(jarPath))
                        return Path.GetFullPath(jarPath);
                    folder = Path.GetDirectoryName(folder);
                }
            }
            catch {
            }
            return null;
        }

        private async Task<string> DownloadPlantUML(string execPath) {
            try {
                SetStatusMsg("PlantUML 다운로드 시작...");
                var downloadFolder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(execPath), PlantUMLFolder));
                if (!Directory.Exists(downloadFolder))
                    Directory.CreateDirectory(downloadFolder);

                var downloadPath = Path.GetFullPath(Path.Combine(downloadFolder, PlantUMLFile));
                using (var client = new WebClient()) {
                    await client.DownloadFileTaskAsync(PlantUMLDownloadURL, downloadPath);
                }

                SetStatusMsg("PlantUML 다운로드 성공", true);
                return downloadPath;
            }
            catch {
                SetStatusMsg("PlantUML 다운로드 실패", false);
            }
            return null;
        }

        private bool FindJavaExec() {
            try {
                var psi = new ProcessStartInfo() {
                    FileName = "where.exe",
                    Arguments = "java.exe",
                    UseShellExecute = false,
                };
                var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch {
                return false;
            }
        }

        private async Task<Tuple<bool, string>> RunPlanetUML(string args) {
            return await Task.Run(() => {
                try {
                    File.WriteAllText(tmpFileStdOut, "", Encoding.UTF8);
                    File.WriteAllText(tmpFileStdErr, "", Encoding.UTF8);
                }
                catch {
                    return Tuple.Create(false, "로그 파일 쓰기 실패");
                }

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                bool success = false;

                try {
                    Process p = new Process {
                        StartInfo = new ProcessStartInfo() {
                            FileName = "java.exe",
                            Arguments = $"-jar {plantUMLPath} {args}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        }
                    };

                    p.OutputDataReceived += (s, e) => { stdout.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { stderr.AppendLine(e.Data); };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();

                    success = p.ExitCode == 0;
                }
                catch {
                    return Tuple.Create(false, "PlantUML 실행 오류");
                }

                try {
                    var stderrValue = stderr.ToString();
                    if (success)
                        success = stderrValue.IndexOf("Exception") < 0;
                    File.WriteAllText(tmpFileStdOut, stdout.ToString(), Encoding.UTF8);
                    File.WriteAllText(tmpFileStdErr, stderrValue, Encoding.UTF8);

                    return Tuple.Create(success, "PlantUML 실행 완료");
                }
                catch {
                    return Tuple.Create(success, "로그 파일 쓰기 실패");
                }
            });
        }

        private async Task<bool> GenerateLiveImage() {
            var (success, _) = await RunPlanetUML($"-tpng -o \"{tmpFolder}\" {inputCharset} \"{sourcePath}\"");
            return success;
        }

        private async void RegenerateLiveImage() {
            while (true) {
                // Notepad++로 저장할 때, 2회의 변경이 노티되어 약간 기다리도록 함
                Thread.Sleep(100);
                var myValue = changedValue;
                SetStatusMsg("최근 내용 처리 중...");
                var success = await GenerateLiveImage();
                SetStatusMsg("최근 내용 처리 완료", success);
                if (Interlocked.CompareExchange(ref changedValue, 0, myValue) == myValue) {
                    Current.Dispatcher.Invoke(() => {
                        if (success)
                            MainWnd?.RefreshLiveImage(tmpFilePath);
                    });
                    return;
                }
            }
        }
        #endregion
    }
}
