# TemplateRobotSL — Шаблон торгового робота для OsEngine

Готовый шаблон для создания собственного торгового робота на платформе [OsEngine](https://github.com/AlexWan/OsEngine). Содержит полную инфраструктуру: управление объёмом с учётом риска, неторговые периоды, стоп-лосс. Вам остаётся только добавить свою торговую логику.

---

## Содержание

- [Что уже реализовано](#что-уже-реализовано)
- [Быстрый старт](#быстрый-старт)
- [Структура файла](#структура-файла)
- [Параметры робота](#параметры-робота)
- [Расчёт объёма](#расчёт-объёма)
- [Стоп-лосс на каждую позицию без гонки состояний](#стоп-лосс-на-каждую-позицию-без-гонки-состояний)
- [Неторговые периоды](#неторговые-периоды)
- [Пошаговое руководство по добавлению стратегии](#пошаговое-руководство-по-добавлению-стратегии)
- [Режимы работы (Regime)](#режимы-работы-regime)
- [Требования](#требования)
- [Видимость репозитория на GitHub](#видимость-репозитория-на-github)
- [Автор](#автор)

---

## Что уже реализовано

| Функциональность | Статус |
|---|---|
| Расчёт объёма по % риска депозита | ✅ Готово |
| Поддержка SPOT / Linear Perpetual | ✅ Готово |
| Поддержка Stocks MOEX | ✅ Готово |
| Поддержка Futures MOEX | ✅ Готово |
| Поддержка Inverse Futures | ✅ Готово |
| Поддержка Bonds MOEX | ✅ Готово |
| Стоп-лосс (CloseAtStop) | ✅ Готово |
| Неторговые периоды (по времени и дням) | ✅ Готово |
| Очистка словаря стопов при неудачном открытии | ✅ Готово |
| Синхронизация параметров GUI | ✅ Готово |
| Логирование событий | ✅ Готово |
| **Торговая логика (сигналы входа/выхода)** | ❌ Ваша реализация |

---

## Быстрый старт

### 1. Скопируйте файл в проект OsEngine

Поместите `TemplateRobot.cs` в папку вашего проекта OsEngine:

```
OsEngine/
└── OsEngine/
    └── Robots/
        └── TemplateRobot.cs   ← сюда
```

### 2. Переименуйте класс под свою стратегию

Выполните замену во всём файле:

| Было | Станет |
|---|---|
| `TemplateRobot` | `MyStrategyRobot` |
| `"TemplateRobot"` | `"MyStrategyRobot"` |

```csharp
// Было:
[Bot("TemplateRobot")]
public class TemplateRobot : BotPanel

// Стало:
[Bot("MyStrategyRobot")]
public class MyStrategyRobot : BotPanel
```

### 3. Реализуйте свою торговую логику

Все места, требующие вашего кода, отмечены комментарием `// TODO:`.

Найти их быстро:
- **VS Code:** `Ctrl+Shift+F` → ввести `TODO`
- **Visual Studio:** `Ctrl+F` → ввести `TODO`

### 4. Скомпилируйте и запустите OsEngine

Соберите проект и выберите своего робота в интерфейсе OsEngine.

---

## Структура файла

```
TemplateRobot.cs
│
├── КОНСТРУКТОР
│   ├── Инициализация неторговых периодов
│   ├── Регистрация параметров GUI
│   ├── TODO: создать параметры индикаторов
│   └── TODO: создать и подключить индикаторы
│
├── ГЛАВНЫЙ ОБРАБОТЧИК СВЕЧИ  (_tab_CandleFinishedEvent)
│   ├── Проверка режима (Regime)
│   ├── Проверка неторгового времени
│   ├── LogicClosePosition — логика закрытия
│   └── LogicOpenPosition  — логика открытия
│
├── LogicOpenPosition         ← TODO: ваши сигналы входа
├── LogicClosePosition        ← TODO: дополнительные условия выхода по сигналу
├── SetStopLoss               — автоматически при открытии позиции (PositionOpeningSuccesEvent)
├── OnOpeningFail             — очистка словаря стопов при неудаче ордера (PositionOpeningFailEvent)
│
└── РАСЧЁТ ОБЪЁМА
    ├── CalcVolume(side, entryPrice, stopPrice)
    ├── GetVolume(...)         — основная логика по всем секциям
    └── GetAssetValue(...)     — баланс по конкретному активу
```

---

## Параметры робота

### Базовые параметры (вкладка "Base")

Параметры регистрируются и отображаются в GUI в следующем порядке:

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Non trade periods` | button | — | Открыть диалог настройки неторговых периодов |
| `Regime` | string | `Off` | Режим работы робота |
| `Time zone UTC` | int | `4` | Часовой пояс для неторговых периодов |
| `Trade Section` | string | `SPOT и LinearPerpetual` | Тип торгуемого инструмента |
| `Deposit Asset` | string | `USDT` | Валюта депозита для расчёта риска |
| `Trade debug log` | string | `Off` | Подробный лог расчёта объёма (`On` / `Off`) |
| `Volume Long (%)` | decimal | `2.5` | Риск на одну сделку лонг, % от депозита |
| `Volume Short (%)` | decimal | `2.5` | Риск на одну сделку шорт, % от депозита |
| `Slippage (%)` | decimal | `0.1` | Проскальзывание, % от цены входа |
| `Fee (%)` | decimal | `0.1` | Комиссия биржи, % (учитывается дважды: вход + выход) |
| `Bond days to maturity` | int | `30` | Минимум дней до погашения облигации для входа |

---

## Расчёт объёма

Объём рассчитывается так, чтобы потенциальный убыток при срабатывании стопа не превышал заданный % депозита.

**Формула:**

```
Риск в деньгах  = Баланс × (Volume% / 100)
Реальный стоп % = Расстояние до стопа% + Проскальзывание% + Комиссия% × 2
Размер позиции  = Риск в деньгах / Реальный стоп%
```

**Как вызывать:**

```csharp
decimal entry     = _tab.PriceBestAsk;
decimal stopPrice = entry * 0.98m;  // стоп на 2% ниже

decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
if (volume <= 0) return;  // риск-менеджер не пропускает сделку

Position pos = _tab.BuyAtLimit(volume, entry);
_stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser, stopPrice);
```

### Поддерживаемые секции

| Секция | Инструменты |
|---|---|
| `SPOT и LinearPerpetual` | Крипто-спот, линейные перпетуалы (Bybit, Binance и др.) |
| `Stocks MOEX` | Акции и фонды Московской биржи |
| `Futures MOEX` | Фьючерсы MOEX (с учётом ГО и шага цены) |
| `InversFutures` | Инверсные фьючерсы (BitMEX, Bybit Inverse) |
| `Bonds MOEX` | Облигации MOEX |

---

## Стоп-лосс на каждую позицию без гонки состояний

### Проблема

OsEngine работает в двух потоках одновременно:

- **Синхронный поток** — `CandleFinishedEvent`, где выставляется ордер на вход (`BuyAtLimit`)
- **Асинхронный поток** — `PositionOpeningSuccesEvent`, который срабатывает позже, когда биржа подтвердила исполнение

Если хранить цену стопа в поле класса (`_stopPrice`), при нескольких одновременных позициях значение из одной сделки перезапишет значение другой — это и есть **гонка состояний (race condition)**. В результате стоп выставляется по неверной цене или не выставляется вовсе.

### Решение — `ConcurrentDictionary<int, decimal> _stopByOrderId`

Шаблон использует `ConcurrentDictionary<int, decimal>`, где **ключ — `NumberUser` ордера на вход**, а значение — цена стопа, рассчитанная именно для этой заявки. `ConcurrentDictionary` обеспечивает потокобезопасность без явных `lock`-ов — это принципиальное отличие от обычного `Dictionary`.

```
Синхронно (CandleFinishedEvent)          Асинхронно (PositionOpeningSuccesEvent)
─────────────────────────────────        ─────────────────────────────────────────
1. Рассчитать stopPrice                  4. Биржа подтвердила исполнение
2. BuyAtLimit → получить pos             5. SetStopLoss(pos) вызван движком
3. Записать:                             6. Прочитать:
   _stopByOrderId.TryAdd(                   _stopByOrderId.TryGetValue(
     pos.OpenOrders[0].NumberUser,            pos.OpenOrders[0].NumberUser,
     stopPrice)                               out decimal stopPrice)
                                          7. Удалить ключ из словаря (TryRemove)
                                          8. CloseAtStop(pos, stopPrice, ...)
```

`NumberUser` присваивается движком в момент создания ордера внутри `BuyAtLimit` — **до отправки на биржу**, поэтому он гарантированно доступен синхронно и уникален для каждой заявки.

### Почему это безопасно при нескольких позициях

Каждая позиция хранит свой стоп под своим уникальным ключом. Удаление записи происходит в `SetStopLoss` сразу после чтения — стопы других позиций при этом не затрагиваются.

```csharp
// Синхронно — после выставления ордера
Position pos = _tab.BuyAtLimit(volume, entry + slippage);
_stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser, stopPrice);

// Асинхронно — когда биржа подтвердила
private void SetStopLoss(Position pos)
{
    int orderKey = pos.OpenOrders[0].NumberUser;

    if (!_stopByOrderId.TryGetValue(orderKey, out decimal stopPrice) || stopPrice <= 0)
    {
        SendNewLogMessage($"[STOP] Стоп не найден для pos#{pos.Number}", LogMessageType.Error);
        return;
    }

    _stopByOrderId.TryRemove(orderKey, out _);  // удаляем только этот ключ

    decimal slippage = stopPrice * (_curSlippagePercent / 100m);
    _tab.CloseAtStop(pos, stopPrice, stopPrice - slippage);  // для лонга
}
```

### Защита от двойного срабатывания

`SetStopLoss` проверяет `pos.StopOrderRedLine > 0` — если стоп уже выставлен (например, событие сработало дважды), повторная установка игнорируется:

```csharp
if (pos.StopOrderRedLine > 0)
{
    SendNewLogMessage($"[STOP] Стоп уже выставлен для pos#{pos.Number}", LogMessageType.System);
    return;
}
```

### Очистка словаря при неудачном открытии

Если ордер на открытие отклонён биржей или отменён до исполнения, `PositionOpeningSuccesEvent` не вызывается и `SetStopLoss` не срабатывает. Чтобы запись в словаре не осталась навсегда, предусмотрен обработчик `OnOpeningFail`:

```csharp
private void OnOpeningFail(Position pos)
{
    if (pos.OpenOrders == null || pos.OpenOrders.Count == 0) return;

    int orderKey = pos.OpenOrders[0].NumberUser;

    if (_stopByOrderId.TryRemove(orderKey, out _))
    {
        SendNewLogMessage(
            $"[STOP] Стоп удалён из словаря (OpeningFail) orderKey={orderKey} pos#{pos.Number}",
            LogMessageType.System);
    }
}
```

### Итог

| Подход | Гонка состояний | Несколько позиций | Утечка при неудаче |
|---|---|---|---|
| Поле класса `_stopPrice` | ❌ Есть | ❌ Стопы перемешиваются | — |
| `Dictionary<int, decimal>` | ⚠️ Нужен `lock` | ✅ Каждая позиция — свой стоп | ❌ Без `OnOpeningFail` |
| `ConcurrentDictionary` + `OnOpeningFail` | ✅ Нет | ✅ Каждая позиция — свой стоп | ✅ Нет |

---

## Неторговые периоды

В конструкторе заданы периоды по умолчанию (можно изменить через кнопку в GUI):

| Период | Время (UTC+4) | Статус |
|---|---|---|
| Ночной | 00:00 — 10:05 | Включён |
| Обеденный | 13:54 — 14:06 | Выключен |
| Вечерний | 18:01 — 23:58 | Включён |
| Суббота | — | Не торгуем |
| Воскресенье | — | Не торгуем |

Настройки сохраняются между запусками и могут быть изменены через диалог прямо во время работы робота.

---

## Пошаговое руководство по добавлению стратегии

### Шаг 1 — Добавить параметры индикатора

В разделе **"// TODO: добавить параметры индикаторов здесь"** в блоке полей класса:

```csharp
private readonly StrategyParameterInt _lengthEma;
private readonly StrategyParameterInt _lengthRsi;
```

В конструкторе:

```csharp
_lengthEma = CreateParameter("EMA Length", 20, 5, 200, 5, "Indicator");
_lengthRsi = CreateParameter("RSI Length", 14, 5, 100, 1, "Indicator");
```

### Шаг 2 — Создать и подключить индикаторы

```csharp
private Aindicator _ema;
private Aindicator _rsi;
```

В конструкторе:

```csharp
_ema = IndicatorsFactory.CreateIndicatorByName("Ema", name + "Ema", false);
_ema = (Aindicator)_tab.CreateCandleIndicator(_ema, "Prime");
((IndicatorParameterInt)_ema.Parameters[0]).ValueInt = _lengthEma.ValueInt;
_ema.Save();

_rsi = IndicatorsFactory.CreateIndicatorByName("RSI", name + "RSI", false);
_rsi = (Aindicator)_tab.CreateCandleIndicator(_rsi, "Second");
((IndicatorParameterInt)_rsi.Parameters[0]).ValueInt = _lengthRsi.ValueInt;
_rsi.Save();
```

### Шаг 3 — Обновлять параметры при изменении в GUI

В методе `OnParametrsChangeByUser()`:

```csharp
((IndicatorParameterInt)_ema.Parameters[0]).ValueInt = _lengthEma.ValueInt;
_ema.Save();
_ema.Reload();

((IndicatorParameterInt)_rsi.Parameters[0]).ValueInt = _lengthRsi.ValueInt;
_rsi.Save();
_rsi.Reload();
```

### Шаг 4 — Реализовать сигнал входа

В методе `LogicOpenPosition()`:

```csharp
private void LogicOpenPosition(List<Candle> candles)
{
    decimal lastPrice = candles[candles.Count - 1].Close;

    decimal emaValue = _ema.DataSeries[0].Last;
    decimal rsiValue = _rsi.DataSeries[0].Last;

    if (emaValue <= 0 || rsiValue <= 0) return;

    // Вход в лонг: цена выше EMA и RSI не перекуплен
    if (_regime.ValueString == "On" || _regime.ValueString == "LONG-POS")
    {
        if (lastPrice > emaValue && rsiValue < 70)
        {
            decimal entry = _tab.PriceBestAsk;
            if (entry <= 0) entry = lastPrice;

            decimal stopPrice = emaValue * 0.99m;  // стоп под EMA

            decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
            if (volume <= 0) return;

            decimal slippage = entry * (_curSlippagePercent / 100m);
            Position pos = _tab.BuyAtLimit(volume, entry + slippage);

            if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
            {
                SendNewLogMessage("[OPEN] BUY — позиция не создана", LogMessageType.Error);
                return;
            }

            _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser, stopPrice);
            SendNewLogMessage($"[OPEN] BUY | entry≈{entry:F4} stop={stopPrice:F4} vol={volume}", LogMessageType.System);
        }
    }
}
```

### Шаг 5 — Добавить дополнительные условия выхода (опционально)

Начальный стоп-лосс уже выставляется автоматически через `SetStopLoss`. В `LogicClosePosition` можно добавить выход по сигналу индикатора — например, закрыть позицию при развороте:

```csharp
private void LogicClosePosition(List<Candle> candles)
{
    List<Position> openPositions = _tab.PositionsOpenAll;

    decimal emaValue = _ema.DataSeries[0].Last;

    for (int i = 0; openPositions != null && i < openPositions.Count; i++)
    {
        Position pos = openPositions[i];
        if (pos.State != PositionStateType.Open) continue;

        decimal lastPrice = candles[candles.Count - 1].Close;

        // Выход из лонга при закрытии цены ниже EMA
        if (pos.Direction == Side.Buy && lastPrice < emaValue)
            _tab.CloseAtMarket(pos, pos.OpenVolume);

        // Выход из шорта при закрытии цены выше EMA
        if (pos.Direction == Side.Sell && lastPrice > emaValue)
            _tab.CloseAtMarket(pos, pos.OpenVolume);
    }
}
```

---

## Режимы работы (Regime)

| Значение | Поведение |
|---|---|
| `Off` | Робот полностью остановлен |
| `On` | Разрешены лонг и шорт (торговая логика определяется в `LogicOpenPosition`) |
| `LONG-POS` | Только лонг |
| `SHORT-POS` | Только шорт |
| `CLOSE-POS` | Только закрывает существующие позиции, новых не открывает |

---

## Требования

- [OsEngine](https://github.com/AlexWan/OsEngine) актуальной версии
- .NET Framework / .NET совместимый с вашей версией OsEngine
- C# 7.0+
- `using System.Collections.Concurrent` — используется для `ConcurrentDictionary`

---

## Автор

Разработано трейдером **SidorenkoVA** — [@si0683](https://t.me/si0683)

---

## Лицензия

Шаблон предоставляется "как есть". Используйте и модифицируйте свободно.

> **Важно:** Торговые роботы несут финансовые риски. Всегда тестируйте стратегию на исторических данных и в режиме Paper Trading перед реальной торговлей.

---

`osengine` `trading-bot` `algorithmic-trading` `csharp` `dotnet` `template` `quant` `robot` `strategy` `stop-loss` `risk-management` `moex` `futures` `spot` `bybit` `binance` `trading-robot` `boilerplate` `osengine-robot` `osengine-template`
