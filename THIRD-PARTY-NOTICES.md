# 第三方代码说明

网易协议实现参考 NeteaseCloudMusicApiEnhanced/api-enhanced：
https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163

涉及 Infrastructure/Protocol/NeteaseCrypto.cs、Infrastructure/Http 下的协议请求组装，以及离线协议向量。
原作者及 Enhanced 贡献者的实现为协议移植依据。运行时使用的 NuGet 包保留各自许可；本地凭据保护不使用这些网易协议算法。

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
