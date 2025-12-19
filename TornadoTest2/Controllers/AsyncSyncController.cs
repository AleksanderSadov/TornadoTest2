namespace TornadoTest2.Controllers
{
    // Простой контроллер для блоков фиксированного размера
    // - Продюсер вызывает OnDataReceived из контекста прерывания/callback (должно быть быстро)
    // - Консюмер вызывает ReadBlock синхронно с таймаутом
    public class AsyncSyncController
    {
        private readonly int _blockSize; // размер одного блока в байтах; определяет фиксированную длину каждого поступающего/читаемого блока
        private readonly byte[][] _buffer; // массив блоков
        private readonly int _capacity; // ёмкость буфера в блоках (максимальное число блоков, которые можно хранить)
        private int _head; // следующая позиция для записи (продюсер)
        private int _tail; // следующая позиция для чтения (консюмер)
        private int _count; // количество занятых блоков в буфере; используется для проверки пустоты/полноты и быстрого пути чтения
        private readonly object _lock = new object(); // синхронизации для защиты критических секций (защищает _head/_tail/_count и операции копирования)
        private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false); // событие для сигнализации о появлении новых данных; консююмер ожидает его с таймаутом
        private long _droppedBlocks; // счётчик отброшенных блоков при переполнении; инкрементируется атомарно через Interlocked

        public AsyncSyncController(int blockSize, int capacityBlocks)
        {
            if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
            if (capacityBlocks <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBlocks));
            _blockSize = blockSize;
            _capacity = capacityBlocks;
            _buffer = new byte[_capacity][];
            for (int i = 0; i < _capacity; i++) _buffer[i] = new byte[_blockSize];
            _head = 0;
            _tail = 0;
            _count = 0;
            _droppedBlocks = 0;
        }

        // Вызывается продюсером (прерывание/callback). Должно выполняться быстро. Копирует входной блок фиксированного размера в кольцевой буфер.
        public void OnDataReceived(byte[] block)
        {
            if (block == null) return;
            if (block.Length != _blockSize) return; // простая проверка

            lock (_lock)
            {
                if (_count == _capacity)
                {
                    // Буфер полон — отбрасываем самый старый блок, чтобы освободить место (простая политика).
                    _tail = (_tail + 1) % _capacity; // % долгая операция в критичном коде, но для _capacity в степенях двойки компилятор может оптимизировать это в битовую маску: https://www.downtowndougbrown.com/2013/01/microcontrollers-interrupt-safe-ring-buffers/ . Пока не проверял, но зафиксировал инфу
                    _count--;
                    Interlocked.Increment(ref _droppedBlocks);
                }

                // копирование в buffer[_head]
                Buffer.BlockCopy(block, 0, _buffer[_head], 0, _blockSize);
                _head = (_head + 1) % _capacity;
                _count++;
                // сигнализируем консюмеру, что данные доступны
                _dataAvailable.Set();
            }
        }

        // Синхронное чтение одного блока. Бросает TimeoutException, если данные не пришли в течение таймаута.
        public byte[] ReadBlock(int timeoutMs)
        {
            // быстрый путь (данные уже есть)
            lock (_lock)
            {
                if (_count > 0)
                {
                    return DequeueLocked();
                }
            }

            // ожидаем данных
            if (!_dataAvailable.WaitOne(timeoutMs))
                throw new TimeoutException();

            // после ожидания повторяем попытку
            lock (_lock)
            {
                if (_count == 0)
                    throw new TimeoutException();
                return DequeueLocked();
            }
        }

        // Извлекает один блок из буфера. Предполагается, что вызов происходит под lock(_lock).
        private byte[] DequeueLocked()
        {
            var result = new byte[_blockSize];
            Buffer.BlockCopy(_buffer[_tail], 0, result, 0, _blockSize);
            _tail = (_tail + 1) % _capacity;
            _count--;
            return result;
        }

        public int AvailableBlocks
        {
            get { lock (_lock) { return _count; } }
        }

        public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);
    }
}
