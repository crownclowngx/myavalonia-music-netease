# 第三方代码说明

网易协议实现参考 NeteaseCloudMusicApiEnhanced/api-enhanced：
https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163

涉及 Infrastructure/Protocol/NeteaseCrypto.cs、XeapiCrypto.cs、Infrastructure/Http 下的协议请求组装，以及离线协议向量。
原作者及 Enhanced 贡献者的实现为协议移植依据。运行时使用的 NuGet 包保留各自许可；本地凭据保护不使用这些网易协议算法。

V2 新增的锁定依赖及 NuGet 元数据声明：

| 包 | 版本 | 许可声明 / 来源 |
| --- | --- | --- |
| LibVLCSharp | 3.10.0 | LGPL-2.1-or-later；[VideoLAN 源码](https://github.com/videolan/libvlcsharp/tree/3.10.0) |
| VideoLAN.LibVLC.Windows | 3.0.23.1 | LGPL-2.1-or-later；[NuGet 包](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.23.1)，原生组件及模块保留各自声明 |
| BouncyCastle.Cryptography | 2.6.2 | MIT；[项目源码](https://github.com/bcgit/bc-csharp) |

应用采用动态 LibVLC 适配；运行库可通过设置选用外部完整目录。源码与锁定版本信息随本说明保留，原生库没有改写。

V7 新增 `Microsoft.Web.WebView2` **1.0.4191.47**，仅使用 Core 与原生 Loader，在用户收藏 / 取消收藏时加载网易官方 Watchman SDK 取得本次令牌。包采用 Microsoft 随包 `LICENSE.txt`，见 [NuGet 版本](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47)；插件资产声明附带该许可。系统 WebView2 Runtime 需另行安装，本仓库不打包浏览器运行库。

网易令牌衔接参考固定提交的 [register_checktoken_v2](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/register_checktoken_v2.js)。官方 SDK 从 `acstatic-dun.126.net` 按需加载，不复制进仓库；本适配不修改浏览器指纹、不读取用户浏览器 Cookie。运行机制及验证边界见音乐库管理契约。

上游许可证原文：

The MIT License (MIT)

Copyright (c) 2013-2022 Binaryify

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
