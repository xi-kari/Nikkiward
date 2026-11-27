# 第三方代码声明

本项目复用了下列开源项目的源码。各项目的原始许可证全文如下，按 MIT
许可证要求随分发一同保留。

## SharpCompress

[SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.12 用于读取和顺序解压游戏基础包。MIT License，Copyright (c) 2025 Adam Hathcock。下列 MIT 许可条款同样适用。

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

## Bundled web fonts

The home skins, feature pages and photo studio load these fonts from the application folder (`Nikkiward/Web/fonts`). The files are the Google Fonts web subsets as distributed. Each copyright line below is the one embedded in the font files.

- Bodoni Moda — Copyright 2020 The Bodoni Moda Project Authors (https://github.com/indestructible-type/Bodoni). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/bodonimoda)
- Caveat — Copyright 2014 The Caveat Project Authors (https://github.com/googlefonts/caveat). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/caveat)
- Cormorant — Copyright 2015 The Cormorant Project Authors (github.com/CatharsisFonts/Cormorant). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/cormorant)
- Fraunces — Copyright 2020 The Fraunces Project Authors (github.com/undercasetype/Fraunces). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/fraunces)
- JetBrains Mono — Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/jetbrainsmono)
- LXGW WenKai TC — Copyright 2024 The LXGW WenKai Project Authors (https://github.com/lxgw/LxgwWenkaiTC). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/lxgwwenkaitc)
- Manrope — Copyright 2019 The Manrope Project Authors (https://github.com/googlefonts/manrope). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/manrope)
- Noto Sans SC — (c) 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'. [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/notosanssc)
- Noto Serif SC — (c) 2017-2024 Adobe (http://www.adobe.com/). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/notoserifsc)
- ZCOOL XiaoWei — Copyright 2018 The ZCOOL XiaoWei Project Authors (https://www.github.com/googlefonts/xiaowei). [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/zcoolxiaowei)

All of these fonts are licensed under the SIL Open Font License 1.1.

SIL Open Font License 1.1
=========================

PREAMBLE

The goals of the Open Font License (OFL) are to stimulate worldwide development of collaborative font projects, to support the font creation efforts of academic and linguistic communities, and to provide a free and open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and redistributed freely as long as they are not sold by themselves. The fonts, including any derivative works, can be bundled, embedded, redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives, however, cannot be released under any other type of license. The requirement for fonts to remain under this license does not apply to any document created using the fonts or their derivatives.

DEFINITIONS
“Font Software” refers to the set of files released by the Copyright Holder(s) under this license and clearly marked as such. This may include source files, build scripts and documentation.

“Reserved Font Name” refers to any names specified as such after the copyright statement(s).

“Original Version” refers to the collection of Font Software components as distributed by the Copyright Holder(s).

“Modified Version” refers to any derivative made by adding to, deleting, or substituting – in part or in whole – any of the components of the Original Version, by changing formats or by porting the Font Software to a new environment.

“Author” refers to any designer, engineer, programmer, technical writer or other person who contributed to the Font Software.


PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining a copy of the Font Software, to use, study, copy, merge, embed, modify, redistribute, and sell modified and unmodified copies of the Font Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components, in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled, redistributed and/or sold with any software, provided that each copy contains the above copyright notice and this license. These can be included either as stand-alone text files, human-readable headers or in the appropriate machine-readable metadata fields within text or binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font Name(s) unless explicit written permission is granted by the corresponding Copyright Holder. This restriction only applies to the primary font name as presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font Software shall not be used to promote, endorse or advertise any Modified Version, except to acknowledge the contribution(s) of the Copyright Holder(s) and the Author(s) or with their explicit written permission.

5) The Font Software, modified or unmodified, in part or in whole, must be distributed entirely under this license, and must not be distributed under any other license. The requirement for fonts to remain under this license does not apply to any document created using the Font Software.

TERMINATION

This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER

THE FONT SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.

ZCOOL XiaoWei、Cormorant、Manrope、LXGW WenKai TC、Caveat、JetBrains Mono、Noto Sans SC、Fraunces 均来自 [Google Fonts](https://github.com/google/fonts/tree/main/ofl)，采用 SIL Open Font License 1.1。
