using KioskBrowser.Shared;

// 用法: CredTool enc|dec <text>
if (args.Length < 2) { Console.WriteLine("usage: CredTool enc|dec <text>"); return 1; }
var r = args[0] == "enc" ? CredentialCrypto.Encrypt(args[1]) : CredentialCrypto.Decrypt(args[1]);
Console.WriteLine(r);
return 0;
