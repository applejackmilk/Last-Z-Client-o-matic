using System.Drawing;

namespace WindowsMCP_Integrated.Core.UI
{
    public abstract class UIContainerAbstract
    {
        protected Rectangle _area;
        private readonly ContainerName container;
        protected List<ElementLocation> _elements;
        public UIContainerAbstract(int x, int y, int width, int height, ContainerName _container)
        {
            _area = new Rectangle(x, y, width, height);
            container = _container;
            _elements = new List<ElementLocation>();
        }

        protected ElementLocation GetElementByIndex(int index)
        {
            if (index < 0 || index >= _elements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of bounds.");
            }
            return _elements[index];
        }

        public ElementLocation CreateChild(string name, int left, int top, int width, int height)
        {
            var knownElement = _elements.FirstOrDefault( e =>
               e.Name == name && e.Size.Width == width && e.Size.Height == height);

            if (knownElement != null)
            {
                return knownElement;
            }

            var newElement = ElementLocation.Create(name
                , new Point(_area.X + left, _area.Y + top)
                , new Size(width, height), container);
            _elements.Add(newElement);
            return newElement;
        }

        protected Size? _defaultChildSize;
        public void AddChild(string name)
        {
            CreateChild(name, _elements.Count);
        }

        public ElementLocation CreateChild(string name, int index)
        {
            if (!_defaultChildSize.HasValue)
            {
                throw new InvalidOperationException("Default child size is not set.");
            }

            return CreateChild(name, index * _defaultChildSize.Value.Width, 0, _defaultChildSize.Value.Width, _defaultChildSize.Value.Height);
        }
    }
}
