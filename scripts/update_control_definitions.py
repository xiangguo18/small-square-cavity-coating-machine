"""Apply the confirmed PLC control-point map to the embedded definition database."""

from __future__ import annotations

import sqlite3
import sys
from pathlib import Path


def main() -> None:
    database = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("Resources/Database/方腔.db")
    connection = sqlite3.connect(database)
    try:
        with connection:
            columns = {row[1] for row in connection.execute("PRAGMA table_info(PartCommand)")}
            if "InterlockNode" not in columns:
                connection.execute("ALTER TABLE PartCommand ADD COLUMN InterlockNode VARCHAR(255) NOT NULL DEFAULT ''")

            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS SystemCommandDef (
                    Name VARCHAR(80) PRIMARY KEY,
                    Chinese NVARCHAR(255) NOT NULL,
                    CommandNode VARCHAR(255) NOT NULL,
                    EnableNode VARCHAR(255) NOT NULL DEFAULT '',
                    FeedbackNode VARCHAR(255) NOT NULL DEFAULT '',
                    RequiresBuiltInAdmin INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS InterlockDef (
                    Id INTEGER PRIMARY KEY,
                    DeviceKey VARCHAR(80) NOT NULL,
                    Action VARCHAR(20) NOT NULL,
                    Chinese NVARCHAR(255) NOT NULL,
                    Address VARCHAR(255) NOT NULL UNIQUE
                );
                """
            )

            # One Part row represents one real Part_State element.  The former
            # 100..110 placeholders are removed now that PLC feedback is known.
            parts = [
                (0, "PCC", "DryPump", "Part_State[0]", ""),
                (1, "PCC", "TurboPump", "Part_State[1]", ""),
                (2, "PCC", "ApcValve", "Part_State[2]", ""),
                (3, "PCC", "Heater", "Part_State[3]", ""),
                (4, "PCC", "SampleStage", "Part_State[4]", ""),
                (5, "PCC", "SampleShutter", "Part_State[5]", ""),
                (6, "PCC", "Power1", "Part_State[6]", ""),
                (7, "PCC", "Power2", "Part_State[7]", ""),
                (11, "PCC", "ArgonLowerValve", "Part_State[11]", ""),
                (12, "PCC", "Cathode1GasValve", "Part_State[12]", ""),
                (13, "PCC", "NitrogenLowerValve", "Part_State[13]", ""),
                (14, "PCC", "Cathode2GasValve", "Part_State[14]", ""),
                (15, "PCC", "OxygenLowerValve", "Part_State[15]", ""),
                (16, "PCC", "Cathode3GasValve", "Part_State[16]", ""),
                (17, "PCC", "ForelineGauge", "Part_State[17]", ""),
                (18, "PCC", "HighVacuumGauge", "Part_State[18]", ""),
                (19, "PCC", "FilmGauge", "Part_State[19]", ""),
                (20, "PCC", "FilmGaugeValve", "Part_State[20]", ""),
                (21, "PCC", "BypassValve", "Part_State[21]", ""),
                (22, "PCC", "ForelineValve", "Part_State[22]", ""),
                (23, "PCC", "VentValve", "Part_State[23]", ""),
                (24, "PCC", "Target1Shutter", "Part_State[24]", ""),
                (25, "PCC", "Target2Shutter", "Part_State[25]", ""),
            ]
            connection.execute("DELETE FROM Part WHERE Id BETWEEN 100 AND 110")
            connection.executemany(
                "INSERT INTO Part(Id,Location,Name,Node,Interlock) VALUES(?,?,?,?,?) "
                "ON CONFLICT(Id) DO UPDATE SET Location=excluded.Location,Name=excluded.Name,Node=excluded.Node,Interlock=excluded.Interlock",
                parts,
            )

            commands = [
                (0, 0, "Start", "干泵启动", "DryPump", ""),
                (1, 0, "Stop", "干泵停止", "DryPump", ""),
                (2, 1, "Start", "分子泵启动", "TurboPump", ""),
                (3, 1, "Stop", "分子泵停止", "TurboPump", ""),
                (4, 1, "LowSpeed", "分子泵低速", "TurboPump", ""),
                (5, 1, "Reset", "分子泵复位", "TurboPump", ""),
                (8, 3, "Start", "加热启动", "Heater", ""),
                (9, 3, "Stop", "加热停止", "Heater", ""),
                (10, 4, "FWD", "样品台正转", "SampleStage", ""),
                (11, 4, "REV", "样品台反转", "SampleStage", ""),
                (12, 4, "STOP", "样品台停止", "SampleStage", ""),
                (13, 5, "Open", "样品挡板打开", "SampleShutter", ""),
                (14, 5, "Close", "样品挡板关闭", "SampleShutter", ""),
                (15, 24, "Open", "靶1挡板打开", "Target1Shutter", ""),
                (16, 24, "Close", "靶1挡板关闭", "Target1Shutter", ""),
                (17, 25, "Open", "靶2挡板打开", "Target2Shutter", ""),
                (18, 25, "Close", "靶2挡板关闭", "Target2Shutter", ""),
                (21, 6, "Start", "直流电源启动", "Power1", ""),
                (22, 6, "Stop", "直流电源停止", "Power1", ""),
                (23, 7, "Start", "中频电源启动", "Power2", ""),
                (24, 7, "Stop", "中频电源停止", "Power2", ""),
                (27, 11, "Open", "Ar气阀打开", "ArgonLowerValve", "EQ_Interlock[10]"),
                (28, 11, "Close", "Ar气阀关闭", "ArgonLowerValve", "EQ_Interlock[11]"),
                (29, 12, "Open", "阴极1气阀打开", "Cathode1GasValve", ""),
                (30, 12, "Close", "阴极1气阀关闭", "Cathode1GasValve", ""),
                (31, 15, "Open", "O₂气阀打开", "OxygenLowerValve", "EQ_Interlock[14]"),
                (32, 15, "Close", "O₂气阀关闭", "OxygenLowerValve", "EQ_Interlock[15]"),
                (33, 14, "Open", "阴极2气阀打开", "Cathode2GasValve", ""),
                (34, 14, "Close", "阴极2气阀关闭", "Cathode2GasValve", ""),
                (35, 13, "Open", "N₂气阀打开", "NitrogenLowerValve", "EQ_Interlock[12]"),
                (36, 13, "Close", "N₂气阀关闭", "NitrogenLowerValve", "EQ_Interlock[13]"),
                (37, 16, "Open", "阴极3气阀打开", "Cathode3GasValve", ""),
                (38, 16, "Close", "阴极3气阀关闭", "Cathode3GasValve", ""),
                (39, 20, "Open", "薄膜真空度计阀打开", "FilmGaugeValve", "EQ_Interlock[4]"),
                (40, 20, "Close", "薄膜真空度计阀关闭", "FilmGaugeValve", "EQ_Interlock[5]"),
                (41, 21, "Open", "旁抽阀打开", "BypassValve", "EQ_Interlock[0]"),
                (42, 21, "Close", "旁抽阀关闭", "BypassValve", "EQ_Interlock[1]"),
                (43, 22, "Open", "前级阀打开", "ForelineValve", "EQ_Interlock[2]"),
                (44, 22, "Close", "前级阀关闭", "ForelineValve", "EQ_Interlock[3]"),
                (45, 23, "Open", "放气阀打开", "VentValve", "EQ_Interlock[8]"),
                (46, 23, "Close", "放气阀关闭", "VentValve", "EQ_Interlock[9]"),
                (980, 499, "PumpStart", "抽真空流程启动", "VacuumWorkflow", ""),
                (981, 499, "VentStart", "破真空流程启动", "VentWorkflow", ""),
                (982, 499, "HoldPressure", "保压流程启动", "HoldWorkflow", ""),
            ]
            connection.execute("DELETE FROM PartCommand WHERE Id=983")
            connection.executemany(
                """
                INSERT INTO PartCommand(Id,PartId,Command,Chinese,Node,EnableNode,Info,InterlockNode)
                VALUES(?,?,?,?,?,?,?,?)
                ON CONFLICT(Id) DO UPDATE SET PartId=excluded.PartId,Command=excluded.Command,
                    Chinese=excluded.Chinese,Node=excluded.Node,EnableNode=excluded.EnableNode,
                    Info=excluded.Info,InterlockNode=excluded.InterlockNode
                """,
                [
                    (index, part, command, chinese, f"Part_Command[{index}]", f"Part_Command_En[{index}]", key, interlock)
                    for index, part, command, chinese, key, interlock in commands
                ],
            )

            connection.executemany(
                "UPDATE PartData SET PartId=?,Name=?,Chinese=?,Type=?,Node=?,SetNode=?,Min=?,Max=?,Unit=? WHERE Id=?",
                [
                    (1, "Speed", "分子泵转速", "RW", "Part_Data1[0]", "Part_Data_Set1[0]", 0, 100, "%", 0),
                    (2, "Opening", "APC阀开度", "RW", "Part_Data1[1]", "Part_Data_Set1[1]", 0, 100, "%", 1),
                    (3, "Temperature", "加热温度", "RW", "Part_Data1[2]", "Part_Data_Set1[2]", 0, 650, "℃", 2),
                    (4, "Speed", "样品台转速", "RW", "Part_Data1[3]", "Part_Data_Set2[2]", 0, 500, "rpm", 3),
                    (4, "UnusedPosition1", "未使用位置1", "R", "Part_Data1[4]", "", -5000, 5000, "mm", 4),
                    (5, "UnusedSpeed2", "未使用速度2", "R", "Part_Data1[5]", "", 0, 500, "mm/s", 5),
                    (5, "UnusedPosition2", "未使用位置2", "R", "Part_Data1[6]", "", -5000, 5000, "mm", 6),
                    (11, "Flow", "Ar当前流量", "RW", "Part_Data1[7]", "Part_Data_Set1[4]", 0, 500, "sccm", 7),
                    (13, "Flow", "N₂当前流量", "RW", "Part_Data1[8]", "Part_Data_Set1[5]", 0, 500, "sccm", 8),
                    (15, "Flow", "O₂当前流量", "RW", "Part_Data1[9]", "Part_Data_Set1[6]", 0, 500, "sccm", 9),
                    (17, "Pressure", "前级真空度", "R", "Part_Data1[10]", "", 0, 150000, "Pa", 10),
                    (18, "Pressure", "高真空度", "R", "Part_Data1[11]", "", 0, 150000, "Pa", 11),
                    (19, "Pressure", "薄膜真空度", "R", "Part_Data1[12]", "", 0, 150000, "Pa", 12),
                    (20, "OpenTime", "薄膜真空度计阀打开时间", "R", "Part_Data1[13]", "", 0, 10, "s", 13),
                    (20, "CloseTime", "薄膜真空度计阀关闭时间", "R", "Part_Data1[14]", "", 0, 10, "s", 14),
                    (21, "OpenTime", "旁抽阀打开时间", "R", "Part_Data1[15]", "", 0, 10, "s", 15),
                    (21, "CloseTime", "旁抽阀关闭时间", "R", "Part_Data1[16]", "", 0, 10, "s", 16),
                    (22, "OpenTime", "前级阀打开时间", "R", "Part_Data1[17]", "", 0, 10, "s", 17),
                    (22, "CloseTime", "前级阀关闭时间", "R", "Part_Data1[18]", "", 0, 10, "s", 18),
                    (23, "OpenTime", "放气阀打开时间", "R", "Part_Data1[19]", "", 0, 10, "s", 19),
                    (23, "CloseTime", "放气阀关闭时间", "R", "Part_Data1[20]", "", 0, 10, "s", 20),
                ],
            )
            power_data = [
                (21, 6, "Power", "直流功率", "RW", "Part_Data1[21]", "Part_Data_Set2[0]", 0, 50000, "W"),
                (22, 6, "Voltage", "直流电压", "R", "Part_Data1[22]", "", 0, 1000, "V"),
                (23, 6, "Current", "直流电流", "R", "Part_Data1[23]", "", 0, 1000, "A"),
                (24, 7, "Power", "中频功率", "RW", "Part_Data1[24]", "Part_Data_Set2[1]", 0, 50000, "W"),
                (25, 7, "Voltage", "中频电压", "R", "Part_Data1[25]", "", 0, 1000, "V"),
                (26, 7, "Current", "中频电流", "R", "Part_Data1[26]", "", 0, 1000, "A"),
            ]
            connection.execute("DELETE FROM PartData WHERE Id BETWEEN 21 AND 26")
            connection.executemany(
                "INSERT INTO PartData(Id,PartId,Name,Chinese,Type,Node,SetNode,Min,Max,Unit) VALUES(?,?,?,?,?,?,?,?,?,?)",
                power_data,
            )

            system_commands = [
                ("Manual", "手动", "EQ_Manual", "EQ_Manual_En", "fbButtonManual_Output", 0),
                ("Auto", "自动", "EQ_Auto", "EQ_Auto_En", "fbButtonAuto_Output", 0),
                ("Semi", "半自动", "EQ_Semi", "EQ_Semi_En", "fbButtonSemi_Output", 0),
                ("Start", "系统开启", "EQ_Start", "EQ_Start_En", "fbButtonStart_Output", 0),
                ("Stop", "系统停止", "EQ_Stop", "EQ_Stop_En", "fbButtonStop_Output", 0),
                ("Reset", "系统复位", "EQ_Reset", "EQ_Reset_En", "fbButtonReset_Output", 0),
                ("PassInterlock", "互锁解除", "EQ_PassInterlock", "", "EQ_PassInterlock", 1),
            ]
            connection.execute("DELETE FROM SystemCommandDef")
            connection.executemany("INSERT INTO SystemCommandDef VALUES(?,?,?,?,?,?)", system_commands)

            interlocks = [
                (0, "BypassValve", "Open", "旁抽阀打开允许", "EQ_Interlock[0]"),
                (1, "BypassValve", "Close", "旁抽阀关闭允许", "EQ_Interlock[1]"),
                (2, "ForelineValve", "Open", "前级阀打开允许", "EQ_Interlock[2]"),
                (3, "ForelineValve", "Close", "前级阀关闭允许", "EQ_Interlock[3]"),
                (4, "FilmGaugeValve", "Open", "薄膜真空度计阀打开允许", "EQ_Interlock[4]"),
                (5, "FilmGaugeValve", "Close", "薄膜真空度计阀关闭允许", "EQ_Interlock[5]"),
                (6, "ApcValve", "Open", "APC阀打开允许", "EQ_Interlock[6]"),
                (7, "ApcValve", "Close", "APC阀关闭允许", "EQ_Interlock[7]"),
                (8, "VentValve", "Open", "放气阀打开允许", "EQ_Interlock[8]"),
                (9, "VentValve", "Close", "放气阀关闭允许", "EQ_Interlock[9]"),
                (10, "ArgonLowerValve", "Open", "Ar气阀打开允许", "EQ_Interlock[10]"),
                (11, "ArgonLowerValve", "Close", "Ar气阀关闭允许", "EQ_Interlock[11]"),
                (12, "NitrogenLowerValve", "Open", "N₂气阀打开允许", "EQ_Interlock[12]"),
                (13, "NitrogenLowerValve", "Close", "N₂气阀关闭允许", "EQ_Interlock[13]"),
                (14, "OxygenLowerValve", "Open", "O₂气阀打开允许", "EQ_Interlock[14]"),
                (15, "OxygenLowerValve", "Close", "O₂气阀关闭允许", "EQ_Interlock[15]"),
            ]
            connection.execute("DELETE FROM InterlockDef")
            connection.executemany("INSERT INTO InterlockDef VALUES(?,?,?,?,?)", interlocks)
    finally:
        connection.close()


if __name__ == "__main__":
    main()
