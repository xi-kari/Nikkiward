# 第三方代码声明

本项目复用了下列开源项目的源码。各项目的原始许可证全文如下，按 MIT
许可证要求随分发一同保留。

## Starward

- 来源：https://github.com/Scighost/Starward（tag 0.18.1）
- 截图编码使用 `Starward.Codec` 0.5.2（MIT），用于 AVIF、JPEG XL 与颜色配置文件写入。
- 许可证：MIT
- 复用范围：`Nikkiward/Features/GamepadControl/` 下的手柄增强实现
  （`GamepadController.cs`、`GamepadKeyNames.cs`），改写自上游
  `src/Starward/Features/GamepadControl/`。已移除截图动作、模拟键鼠输入提示
  浮层、本地化资源查找与上游的 DI/配置层。

```
MIT License

Copyright (c) 2023 Scighost

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Starward.Codec native runtime

`Starward.Codec` 0.5.2 的 `win-x64` native runtime 会随便携 ZIP 和安装包一起分发
下列上游组件。它们用于图片编码、色彩配置文件和视频解码；本项目不修改这些
二进制文件。对应的上游许可证和版权声明应与组件一起保留：

- [libavif](https://github.com/AOMediaCodec/libavif)：BSD 2-Clause
- [libjxl](https://github.com/libjxl/libjxl)：BSD 3-Clause
- [libultrahdr](https://github.com/google/libultrahdr)：Apache License 2.0
- [Little CMS](https://github.com/mm2/Little-CMS)：MIT
- [libvpx](https://chromium.googlesource.com/webm/libvpx/)：BSD 3-Clause
- [Brotli](https://github.com/google/brotli)：MIT
- [libyuv](https://chromium.googlesource.com/libyuv/libyuv/)：BSD 3-Clause
- Ogg/Vorbis runtime：BSD-style license

组件版本和来源以 `Starward.Codec.nuspec`（package 0.5.2）记录为准；其原生
工具程序（`avifdec.exe`、`avifenc.exe`、`avifgainmaputil.exe`、`cjxl.exe`、
`djxl.exe`、`jxlinfo.exe`）属于同一依赖的 runtime 文件，不是 Nikkiward 插件。

## NikkiGallery native metadata library

- 来源：https://github.com/QianQianLuLu1/NikkiGallery
- 固定提交：`ca8ac9fbc97d449ebc8dc8d08997c93b00a882e9`
- 文件：`resources/nuan5_decryption.dll`
- SHA-256：`3F0D88A2510106FF8E66A4730A77EF9F7FFC27C89411F81FA223CC3E1170E601`
- 许可证：MIT，Copyright (c) 2026 QianLu
- 用途：只读相册游戏内参数解析；Nikkiward 通过已审计的 C ABI 调用，不包含
  NikkiGallery 的 Electron、SQLite 或账号功能。
- 来源边界：DLL 为上游仓库提供的未签名 x64 二进制；本项目不声称其独立构建、
  签名或额外审计来源。打包时仅随 x64 native runtime 路径复制，并在 DLL 缺失、架构
  不符或 ABI 不匹配时退回无参数显示。

```
MIT License

Copyright (c) 2026 QianLu

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## React Bits Border Glow

- 来源：https://github.com/DavidHDev/react-bits
- 固定提交：`4e0e030193b563be6be33d928f77d0d01cefe237`
- 上游文件：`src/ts-default/Components/BorderGlow/BorderGlow.tsx`、
  `src/ts-default/Components/BorderGlow/BorderGlow.css`
- 许可证：MIT + Commons Clause License Condition v1.0
- 复用范围：`Nikkiward/Controls/CardBorderGlow.cs` 与
  `Nikkiward/Controls/CardBorderGlowProjection.cs` 将上游的边缘接近度、指针角度、
  悬停显隐和局部边缘光算法移植到 WinUI 3 / Win2D；不包含 React 运行时或网页代码。

```
MIT + Commons Clause License Condition v1.0

Copyright (c) 2026 David Haz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, and distribute the Software as part of
an application, website, or product, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

Commons Clause Restriction

You may use this Software, including for any commercial purpose, so long as
you do not sell, sublicense, or redistribute the components themselves,
whether alone, in a bundle, or as a ported version.

No Warranty

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
