using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace KFTCTaxCAP.Services;

/// <summary>
/// 홈 화면·기동 로그에 쓰는 버전 문자열(docs/home_reader_setup/PRD_WPF.md §3.2, 2026-10-15).
/// 앱 버전은 프로젝트 파일 <c>&lt;Version&gt;</c>, 테스트 표시는 <c>&lt;KftcReleaseLabel&gt;</c>, 리더기 모듈 버전은
/// 실행 폴더 <c>ReaderSerial.dll</c>의 파일 버전에서 읽는다. 화면 문구에 버전 리터럴을 두지 않는다.
/// </summary>
public static class AppVersionInfo
{
    private static readonly Assembly AppAssembly = typeof(AppVersionInfo).Assembly;

    /// <summary>주.부.수정 세 자리(예: <c>1.0.0</c>).</summary>
    public static string AppVersion { get; } = AppAssembly.GetName().Version?.ToString(3) ?? "-";

    /// <summary>테스트 표시(예: <c>테스트</c>). 정식 배포본은 빈 문자열.</summary>
    public static string ReleaseLabel { get; } =
        AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "KftcReleaseLabel")?.Value ?? string.Empty;

    /// <summary>빌드 정보(예: <c>1.0.0+70c21c6</c>) — 설치 파일 빌드는 커밋 번호가 붙는다. 기동 로그용.</summary>
    public static string InformationalVersion { get; } =
        AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? AppVersion;

    /// <summary>ReaderSerial.dll 파일 버전 원문(예: <c>1.0.0.0</c>). 읽지 못하면 <c>-</c>.</summary>
    public static string ReaderModuleFileVersion { get; } = ReadReaderModuleFileVersion();

    /// <summary><c>Ver 1.0.0 (테스트) | 리더기 모듈 Ver 1.0.0.0</c> — 타이틀바·서브타이틀 공통 뒷부분.
    /// 리더기 모듈은 파일 버전 네 자리를 그대로 쓴다(2026-10-15 사용자 결정).</summary>
    public static string VersionText { get; } =
        $"Ver {AppVersion}{(ReleaseLabel.Length > 0 ? $" ({ReleaseLabel})" : string.Empty)} | 리더기 모듈 Ver {ReaderModuleFileVersion}";

    private static string ReadReaderModuleFileVersion()
    {
        try
        {
            string dir = Path.GetDirectoryName(AppAssembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(dir, "ReaderSerial.dll");
            if (!File.Exists(path))
                return "-";

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            // 버전 리소스가 없는 DLL(2026-10-15 이전 빌드)은 네 자리가 모두 0이다.
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0 && info.FilePrivatePart == 0)
                return "-";

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch
        {
            return "-";
        }
    }
}
