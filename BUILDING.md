# 构建说明 / Building

本插件需要引用**游戏侧的 dll**（BepInEx 核心、反编译生成的 interop、Unity 库）。
这些文件**不随仓库分发**（版权与体积原因），需要你在本地指定路径。

---

## 一、前置条件

| 项 | 版本 |
|---|---|
| .NET SDK | **6.0 或更高**（本项目 `net6.0`）|
| Limbus Company | 已安装 |
| BepInEx 6（LimiNex） | 已装进游戏目录 |
| ModularSkillScripts | 已放在 `BepInEx/plugins/` |

> 首次启动游戏后，BepInEx 会自动生成 `BepInEx/interop/` 与 `BepInEx/unity-libs/`。
> 没有这两个目录就先启动一次游戏。

---

## 二、指定游戏路径（二选一）

### 方式 A：环境变量（推荐，一次设置）

```bat
setx LIMBUS_BEPINEX  "D:\Steam\steamapps\common\Limbus Company\BepInEx"
setx LIMBUS_INTEROP "%LIMBUS_BEPINEX%\interop"
setx LIMBUS_UNITYLIB "%LIMBUS_BEPINEX%\unity-libs"
```

> `setx` 只影响**之后新开的**终端。设置完请关掉再重开命令行。

PowerShell 版本：

```powershell
[Environment]::SetEnvironmentVariable("LIMBUS_BEPINEX",  "D:\...\BepInEx", "User")
[Environment]::SetEnvironmentVariable("LIMBUS_INTEROP",  "D:\...\BepInEx\interop", "User")
[Environment]::SetEnvironmentVariable("LIMBUS_UNITYLIB", "D:\...\BepInEx\unity-libs", "User")
```

### 方式 B：本地 props 文件（只影响本机）

在**仓库根目录**新建 `LocalPaths.props`（已在 `.gitignore` 中，不会被提交）：

```xml
<Project>
  <PropertyGroup>
    <LimbusBepInEx>D:\Steam\steamapps\common\Limbus Company\BepInEx</LimbusBepInEx>
    <LimbusInterop>D:\Steam\steamapps\common\Limbus Company\BepInEx\interop</LimbusInterop>
    <LimbusUnityLib>D:\Steam\steamapps\common\Limbus Company\BepInEx\unity-libs</LimbusUnityLib>
  </PropertyGroup>
</Project>
```

---

## 三、编译

```bat
dotnet build src\CustomBreakRecoverAnimation.csproj -c Release
```

产物：

```
src\bin\Release\CustomBreakRecoverAnimation.dll
```

复制到 `BepInEx\plugins\` 即可。

### 一行完成编译 + 部署（PowerShell）

```powershell
dotnet build src\CustomBreakRecoverAnimation.csproj -c Release -v q --nologo
Copy-Item src\bin\Release\CustomBreakRecoverAnimation.dll "$env:LIMBUS_BEPINEX\plugins\" -Force
```

---

## 四、常见问题

| 症状 | 原因 / 解决 |
|---|---|
| `CS0246: 找不到类型 BepInEx` | 环境变量没生效。关掉终端重开，或改用方式 B |
| `CS0006: 未能找到元数据文件 Assembly-CSharp.dll` | `LIMBUS_INTEROP` 指错，或 `interop` 目录还没生成（先启动一次游戏）|
| 编译通过但游戏里没反应 | DLL 没放进 `plugins`，或 ModularSkillScripts 版本低于 5.3.5 |
| 游戏启动报版本不匹配 | 游戏更新后 interop 需要重新生成（删掉 `interop` 目录后重启游戏）|

---

## 五、关于 .NET 版本

本项目目标框架是 `net6.0`（与 BepInEx 6 的运行环境一致）。
用 .NET 8 SDK 编译也可以 —— SDK 8 能编译 net6.0 目标，只要机器上装了 .NET 6 运行时。
