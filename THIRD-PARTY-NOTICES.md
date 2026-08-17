# 第三方代码声明

本项目复用了下列开源项目的源码。各项目的原始许可证全文如下，按 MIT
许可证要求随分发一同保留。

## Starward

- 来源：https://github.com/Scighost/Starward（tag 0.18.1）
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
