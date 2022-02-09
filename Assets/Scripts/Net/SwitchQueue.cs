using System.Collections.Generic;

namespace Net
{
    public class SwitchQueue<T>
    {
        private readonly object locker = new object();
        private Queue<T> consumeQueue; //消费
        private Queue<T> produceQueue; //生产

        public SwitchQueue()
        {
            consumeQueue = new Queue<T>();
            produceQueue = new Queue<T>();
        }

        public SwitchQueue(int capacity)
        {
            consumeQueue = new Queue<T>(capacity);
            produceQueue = new Queue<T>(capacity);
        }

        public void Enqueue(T obj)
        {
            //Switch过程中可能会Enqueue，需要加锁
            lock (locker)
            {
                produceQueue.Enqueue(obj);
            }
        }

        public T Dequeue()
        {
            //Switch结束之后才会Dequeue，无需加锁
            return consumeQueue.Dequeue();
        }

        public bool Empty()
        {
            return consumeQueue.Count == 0;
        }

        public void Switch()
        {
            lock (locker)
            {
                var temp = consumeQueue;
                consumeQueue = produceQueue;
                produceQueue = consumeQueue;
            }
        }

        public void Clear()
        {
            lock (locker)
            {
                consumeQueue.Clear();
                produceQueue.Clear();
            }
        }
    }
}