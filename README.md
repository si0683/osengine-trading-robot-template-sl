# TemplateRobot — Шаблон торгового робота для OsEngine

Готовый шаблон для создания собственного торгового робота на платформе [OsEngine](https://github.com/AlexWan/OsEngine). Содержит полную инфраструктуру: управление объёмом с учётом риска, неторговые периоды, стоп-лоссы и трейлинг-стопы. Вам остаётся только добавить свою торговую логику.

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
| Начальный стоп-лосс (CloseAtStop) | ✅ Готово |
| Трейлинг-стоп (CloseAtTrailingStop) | ✅ Готово (шаблон) |
| Неторговые периоды (по времени и дням) | ✅ Готово |
| Синхронизация параметров GUI | ✅ Готово |
| Логирование событий | ✅ Готово |
| **Торговая логика (сигналы входа/выхода)** | ❌ Нужно реализовать |

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
├── LogicClosePosition        ← TODO: ваш трейлинг/выход
├── SetStopLoss               — автоматически при открытии позиции
│
└── РАСЧЁТ ОБЪЁМА
    ├── CalcVolume(side, entryPrice, stopPrice)
    ├── GetVolume(...)         — основная логика по всем секциям
    └── GetAssetValue(...)     — баланс по конкретному активу
```

---

## Параметры робота

### Базовые параметры (вкладка "Base")

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Regime` | string | `Off` | Режим работы робота |
| `Time zone UTC` | int | `4` | Часовой пояс для неторговых периодов |
| `Non trade periods` | button | — | Открыть диалог настройки неторговых периодов |

### Параметры объёма (вкладка "Base")

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Trade Section` | string | `SPOT и LinearPerpetual` | Тип торгуемого инструмента |
| `Deposit Asset` | string | `USDT` | Валюта депозита для расчёта риска |
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
_stopByOrderId[pos.OpenOrders[0].NumberUser] = stopPrice;
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

### Решение — словарь `_stopByOrderId`

Шаблон использует `Dictionary<int, decimal>`, где **ключ — `NumberUser` ордера на вход**, а значение — цена стопа, рассчитанная именно для этой заявки.

```
Синхронно (CandleFinishedEvent)          Асинхронно (PositionOpeningSuccesEvent)
─────────────────────────────────        ─────────────────────────────────────────
1. Рассчитать stopPrice                  4. Биржа подтвердила исполнение
2. BuyAtLimit → получить pos             5. SetStopLoss(pos) вызван движком
3. Записать:                             6. Прочитать:
   _stopByOrderId[                          _stopByOrderId.TryGetValue(
     pos.OpenOrders[0].NumberUser             pos.OpenOrders[0].NumberUser,
   ] = stopPrice;                             out decimal stopPrice)
                                          7. Удалить ключ из словаря
                                          8. CloseAtStop(pos, stopPrice, ...)
```

`NumberUser` присваивается движком в момент создания ордера внутри `BuyAtLimit` — **до отправки на биржу**, поэтому он гарантированно доступен синхронно и уникален для каждой заявки.

### Почему это безопасно при нескольких позициях

Каждая позиция хранит свой стоп под своим уникальным ключом. Удаление записи происходит в `SetStopLoss` сразу после чтения — стопы других позиций при этом не затрагиваются.

```csharp
// Синхронно — после выставления ордера
Position pos = _tab.BuyAtLimit(volume, entry + slippage);
_stopByOrderId[pos.OpenOrders[0].NumberUser] = stopPrice;

// Асинхронно — когда биржа подтвердила
private void SetStopLoss(Position pos)
{
    int key = pos.OpenOrders[0].NumberUser;

    if (!_stopByOrderId.TryGetValue(key, out decimal stopPrice) || stopPrice <= 0)
    {
        SendNewLogMessage($"[STOP] Стоп не найден для pos#{pos.Number}", LogMessageType.Error);
        return;
    }

    _stopByOrderId.Remove(key);  // удаляем только этот ключ

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

### Итог

| Подход | Гонка состояний | Несколько позиций |
|---|---|---|
| Поле класса `_stopPrice` | ❌ Есть | ❌ Стопы перемешиваются |
| Словарь `_stopByOrderId` | ✅ Нет | ✅ Каждая позиция — свой стоп |

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

            _stopByOrderId[pos.OpenOrders[0].NumberUser] = stopPrice;
            SendNewLogMessage($"[OPEN] BUY | entry≈{entry:F4} stop={stopPrice:F4} vol={volume}", LogMessageType.System);
        }
    }
}
```

### Шаг 5 — Реализовать трейлинг-выход

В методе `LogicClosePosition()`:

```csharp
private void LogicClosePosition(List<Candle> candles)
{
    List<Position> openPositions = _tab.PositionsOpenAll;

    decimal trailLevel = _ema.DataSeries[0].Last;  // трейлинг по EMA

    for (int i = 0; openPositions != null && i < openPositions.Count; i++)
    {
        Position pos = openPositions[i];
        if (pos.State != PositionStateType.Open) continue;

        if (pos.Direction == Side.Buy && trailLevel > pos.EntryPrice)
        {
            // Прибыль защищена — переходим на трейлинг
            _tab.CloseAtTrailingStop(pos, trailLevel, trailLevel);
        }
    }
}
```

---

## Режимы работы (Regime)

| Значение | Поведение |
|---|---|
| `Off` | Робот полностью остановлен |
| `On` | Открывает лонг и шорт |
| `LONG-POS` | Только лонг |
| `SHORT-POS` | Только шорт |
| `CLOSE-POS` | Только закрывает существующие позиции, новых не открывает |

---

## Требования

- [OsEngine](https://github.com/AlexWan/OsEngine) актуальной версии
- .NET Framework / .NET совместимый с вашей версией OsEngine
- C# 7.0+

---

## Видимость репозитория на GitHub

Чтобы репозиторий находили другие трейдеры и разработчики, стоит потратить 10 минут на несколько простых шагов.

### Название репозитория

Название имеет наибольший вес при ранжировании в поиске GitHub. `TemplateRobot` — короткое, но неинформативное: непонятно ни платформа, ни язык, ни назначение. Рекомендуемые варианты:

| Вариант | Почему хорош |
|---|---|
| `osengine-trading-robot-template` | Содержит все ключевые слова: платформа, сфера, тип, назначение |
| `osengine-strategy-template` | Акцент на стратегии |
| `osengine-bot-framework` | Подходит, если планируется расширение |
| `osengine-robot-boilerplate` | Классический термин для шаблонов |

### Чек-лист видимости

| Что сделать | Как |
|---|---|
| **Описание (Description)** | Под названием репозитория заполните поле: `Шаблон торгового робота для OsEngine с управлением рисками и стоп-лоссами` |
| **Темы (Topics)** | Нажмите шестерёнку рядом с "Topics" и добавьте: `osengine`, `trading-bot`, `algorithmic-trading`, `csharp`, `template`, `quant` |
| **README** | Используйте ключевые слова в заголовках — GitHub индексирует содержимое README |

Темы кликабельны: пользователи, которые фильтруют по `osengine` или `trading-bot`, сразу попадут на ваш репозиторий.

---

## Автор

Разработано трейдером **SidorenkoVA** — [@si0683](https://t.me/si0683)

---

## Лицензия

Шаблон предоставляется "как есть". Используйте и модифицируйте свободно.

> **Важно:** Торговые роботы несут финансовые риски. Всегда тестируйте стратегию на исторических данных и в режиме Paper Trading перед реальной торговлей.
