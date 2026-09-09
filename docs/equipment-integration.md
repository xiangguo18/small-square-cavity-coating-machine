# IO只读监视与参数单项写入

## 操作方式

- I/O状态：显示方腔.db内64个点，支持全部/IN/OUT筛选。亮灯只代表true/1，灭灯代表false/0，不代表正常或故障；无效、断线显示灰灯和“—”。包括OUT在内均无写入入口。
- 参数设置：显示21个参数。查看无需登录；仅现有ParameterSettings权限账号可修改。输入后Enter提交当前行，Esc取消当前草稿，失焦不提交，无批量提交、无确认弹窗。
- PLC当前值、编辑草稿、本机参考值分开显示。未连接时仍有定义和明确标注的参考值，不显示伪实时值，不自动下发数据库Value、DefaultValue或缓存。
- 地址、英文名、位置、分类、默认值在中文参数名提示中查看。范围及单位来自数据库；Parameter.Type仅为分类。
- 全局只允许一笔参数写入在途，此时其他提交和Endpoint切换均被拒绝，而非排队重放。
- 点击任一页面左下角查看连接及各组详情。所有用户可改Endpoint，但写入处理中不可切换。成功会话显示“PLC已连接”，组状态独立列出。

## 配置与点位

方腔.db是SQLite点位定义库，不是OPC UA服务器本身。真实值与写入均经过OPC UA。

| 数据组 | 定义数 | 真实数组下标 | 访问 |
| --- | ---: | --- | --- |
| EventDef / EQ_Alarm | 104 | 0～51、500～551 | 只读 |
| IO / EQ_IO | 64 | 0～31、500～531 | 只读 |
| Parameter / EQ_Parameter1 | 21 | 0～20 | 按授权单元素写入 |

沿用 `%LocalAppData%/SmallSquareCavityCoatingMachine/alarms.opcua.json`，
默认 `opc.tcp://192.168.0.10:4840`，匿名、SecurityMode=None，无加密，只用于可信隔离设备网络。
上位机账号权限不等于服务器访问控制，不应将该端点暴露到不可信网络。

服务从Objects递归完整浏览、精确匹配BrowseName、验证可读数组及UserAccessLevel，从服务器返回NodeId，不拼厂商前缀或Namespace。
缺失、歧义、类型/质量异常分别报告；IO/参数单组初始化失败不会终止有效报警。
初次读取完整数组，随后同一Session/Subscription内监视报警、IO、参数及配方点；采样100ms、发布250ms为请求值，可由服务器修订。
IO至少532元素、参数至少21元素；稀疏下标不压缩、不按数据库行号编号。
IO支持Boolean/严格0、1整数；参数支持Float、Double及8/16/32/64位有符号/无符号整数，以服务器类型为准，不做单位换算。

断线或切换地址：值立即过期，保留草稿但旧会话草稿不能提交到新会话；Esc重新核对后输入。退休会话通知不再生效，待确认操作不自动重试、回滚或重放。

## 写入事务边界

1. 服务端入口再次检查当前账号权限、连接代次、质量、可写性、数字格式及数据库范围；拒绝NaN/无穷大、整数小数和转换后越界。
2. 重新读取该元素，与开始编辑时的原值按实际PLC类型比较；变化则拒绝覆盖。此为写前冲突检查，不是PLC原子compare-and-swap。
3. 将待处理审计同步持久化，记录用户、Endpoint、地址、原值、目标值和时间。落盘失败不发送。
4. 对数组Value属性设置IndexRange为该下标，值为正确PLC类型的长度1数组。禁止整数组覆盖；不支持IndexRange时明确失败。
5. Write返回Good仅为已发送，5秒内回读与目标按实际类型核对；一致才标记“写入已确认（PLC变量值）”。
6. 明确拒绝、回读不符/超时、坏质量、中途断线分别提示。可能已执行但无法确认标记“结果未确认”；不自动重试或回滚。
7. PLC确认后保存缓存和最终审计。最终保存失败时保留PLC已确认结论，并另报本机保存失败。

使用本机控制技能的权限检查、命令/反馈区分、先审计和回读确认原则。按已确认约定不引入触摸屏控制权握手或额外提交位，也不擅自添加/绕过PLC联锁。运行中允许修改不等于参数不会影响设备，现场需由工程师核对允许范围及保护。

## MVVM与文件

- Models/Equipment：只读定义、点值/质量/会话代次、写入结果。
- SqliteEquipmentDefinitionRepository：严格ReadOnly读取内嵌定义。
- IOpcUaEquipmentClient / OpcUaEquipmentClient：唯一共享连接、组状态、写入协调。
- OpcUaEquipmentSession：协议浏览、读取、设备监视项、单元素IndexRange写入。
- EquipmentAlarmSignalSource：保留原报警服务接口的适配器。
- EquipmentRuntimeRepository：独立运行SQLite；不修改方腔.db。
- IoStatusViewModel、ParameterSettingsViewModel/ParameterRowViewModel：集合、草稿、命令与权限状态。
- ParameterEditorBehavior：仅把键盘/焦点事件转为命令，不含PLC/数据库逻辑。
- 后台通知和写入完成通过IUiDispatcher更新界面；退出解除订阅并停止会话。

真实运行记录：`%LocalAppData%/SmallSquareCavityCoatingMachine/equipment-runtime.db`。
Operations保存操作审计，ParameterCache以Endpoint+Address区分已确认值；操作历史页支持重启查询。
上次退出留下“待处理”的记录，下次启动转为“结果未确认”，绝不自动重放。
模拟模式使用独立 `equipment-runtime.simulation.db`，审计标注“模拟”，真实模式不注入模拟数据。

原始方腔.db不改写。内嵌资源已按配方任务同步新版MultiRecipeDef，报警、IO和参数定义不变。当前资源SHA256：
`ec2722d62769d52ed5a67c2d2602958b32cac76abc1aefdfaa7880dd039afb3b`。

## 验证与现场边界

```powershell
dotnet test 'Tests/SmallSquareCavityCoatingMachine.Tests.csproj' --no-restore -c Release
dotnet build 'Small square cavity coating machine.csproj' --no-restore -c Release
# 明确离线模式；不连接真实PLC
dotnet run --project 'Small square cavity coating machine.csproj' -- --simulate-alarms
```

测试包括定义/哈希、稀疏地址、共享订阅、故障隔离、类型范围、原值冲突、权限变更、单笔互斥、旧会话隔离、审计失败、重启缓存、真实WPF绑定与Enter/Esc、500ms通知到显示目标。
官方SDK测试服务器只监听127.0.0.1临时端口，验证真实协议浏览/订阅/单元素写入及回读、相邻值不变、服务器拒绝后无整数组降级。测试不构造生产App、不读取实际Endpoint配置、不向现场PLC写入。
WPF快照输出到测试bin目录的alarm-snapshots（含io-live、parameters-confirmed、parameters-guest、equipment-connection）。

现场仍需确认各组唯一可浏览数组、数据类型、可写权限、允许范围和IndexRange支持；在另行明确授权后再逐项进行现场参数写入验收。软件回读确认不等于电气动作或工艺完成，PLC到界面总延迟需现场实测。

协议依据：[OPC UA Write](https://reference.opcfoundation.org/specs/OPC-10000-4/5.11.4)、[NumericRange](https://reference.opcfoundation.org/specs/OPC-10000-4/7.27)。
配方逐层自动下发和与参数写入的互斥规则见 [recipe-integration.md](recipe-integration.md)。
