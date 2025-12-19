using System;
using System.Threading;

namespace TornadoTest2.Controllers
{
    // Простой контроллер для блоков фиксированного размера
    // - Продюсер вызывает OnDataReceived из контекста прерывания/callback (должно быть быстро)
    // - Консюмер вызывает ReadBlock синхронно с таймаутом
    public class AsyncSyncController
    {
        private readonly int _blockSize;
        private readonly byte[][] _buffer; // массив блоков
        private readonly int _capacity;
        private int _head; // следующая позиция для записи (продюсер)
        private int _tail; // следующая позиция для чтения (консюмер)
        private int _count;
        private readonly object _lock = new object();
        private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false);
        private long _droppedBlocks;

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
                    _tail = (_tail + 1) % _capacity;
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
                    var result = new byte[_blockSize];
                    Buffer.BlockCopy(_buffer[_tail], 0, result, 0, _blockSize);
                    _tail = (_tail + 1) % _capacity;
                    _count--;
                    return result;
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
                var result = new byte[_blockSize];
                Buffer.BlockCopy(_buffer[_tail], 0, result, 0, _blockSize);
                _tail = (_tail + 1) % _capacity;
                _count--;
                return result;
            }
        }

        public int AvailableBlocks
        {
            get { lock (_lock) { return _count; } }
        }

        public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);
    }
}
