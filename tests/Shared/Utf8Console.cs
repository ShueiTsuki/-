using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// 输出被重定向（被 run_all.ps1 捕获）时，stdout 固定用 UTF-8 写。
///
/// 不这样做的后果：默认编码跟随控制台代码页（中文 Windows 是 936/GBK），
/// 而捕获方按什么解码取决于它从哪里被启动 —— 同一份测试输出在一个终端里能解析出
/// 「通过 N / 失败 M」，换个终端就成了乱码，run_all 判为「解析不到结果」。
/// 这里刻意不设 Console.OutputEncoding：那会调用 SetConsoleOutputCP，改掉用户终端的代码页。
/// </summary>
static class Utf8Console
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (!Console.IsOutputRedirected) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
    }
}
