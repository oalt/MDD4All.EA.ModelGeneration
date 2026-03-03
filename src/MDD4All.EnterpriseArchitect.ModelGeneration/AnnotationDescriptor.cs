using MOF = MDD4All.EMOF.DataModels;

namespace MDD4All.EMOF.DotNetToEmofConverter
{
    public class AnnotationDescriptor
    {
        public EA.Element Element { get; set; }

        public EA.Attribute Attribute { get; set; }

        public MOF.Property Property { get; set; }
    }
}
