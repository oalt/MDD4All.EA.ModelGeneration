using MDD4All.EMOF.DotNetToEmofConverter;
using MDD4All.EnterpriseArchitect.Manipulations;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MOF = MDD4All.EMOF.DataModels;

namespace MDD4All.EnterpriseArchitect.ModelGeneration
{
    public class MetamodelFromEmofGenerator
    {
        private EA.Repository _repository;

        private string _pathToSchema;

        private EA.Package _targetPackage;

        private MOF.EmofRepository? _emofRepository;

        private Dictionary<string, EA.Element> _generatedElements = new Dictionary<string, EA.Element>();

        private List<AnnotationDescriptor> _unconnectedPrimitiveAnnotations = new List<AnnotationDescriptor>();


        public MetamodelFromEmofGenerator(EA.Repository repository,
                                          string pathToShema,
                                          EA.Package targetPackage)
        {
            _repository = repository;
            _pathToSchema = pathToShema;
            _targetPackage = targetPackage;
        }

        public void ConvertEmofToMetamodel()
        {

            _generatedElements = new Dictionary<string, EA.Element>();
            _unconnectedPrimitiveAnnotations = new List<AnnotationDescriptor>();

            string emofJson = File.ReadAllText(_pathToSchema);

            _emofRepository = JsonConvert.DeserializeObject<MOF.EmofRepository>(emofJson,
                                                                                new JsonSerializerSettings
                                                                                {
                                                                                    TypeNameHandling = TypeNameHandling.Auto
                                                                                });

            if (_emofRepository != null)
            {
                EA.Diagram metamodelDiagram = _targetPackage.AddDiagram("Class");

                foreach (MOF.Package package in _emofRepository.RootPackages)
                {
                    GeneratePackagesAndElementsRecursively(package, _targetPackage);
                }
                foreach (MOF.Package package in _emofRepository.RootPackages)
                {
                    GenerateConnectorsRecursively(package);
                }

                GenerateAnnotationsForPrimitiveTypes();

                _repository.GetProjectInterface().LayoutDiagram(metamodelDiagram.DiagramGUID, 0);

                _targetPackage.Element.Update();
            }
        }



        private void GeneratePackagesAndElementsRecursively(MOF.Package currentPackage,
                                                            EA.Package parentPackage)
        {
            EA.Package childPackage = parentPackage.GetChildPackageByName(currentPackage.Name);

            if (childPackage == null)
            {
                childPackage = parentPackage.AddChildPackage(currentPackage.Name);
            }

            foreach (MOF.Base.PackageableElement packageableElement in currentPackage.PackagedElements)
            {
                if (packageableElement is MOF.Class)
                {
                    MOF.Class mofClass = (MOF.Class)packageableElement;
                    EA.Element classElement = childPackage.AddElement(mofClass.Name, "Class");

                    if (mofClass.IsAbstract)
                    {
                        classElement.Abstract = "1";
                        classElement.Update();
                    }

                    AddPrimitiveProperties(mofClass.OwnedAttributes, classElement);

                    if (!_generatedElements.ContainsKey(mofClass.FullName))
                    {
                        _generatedElements.Add(mofClass.FullName, classElement);
                    }
                }
                else if (packageableElement is MOF.Interface)
                {
                    MOF.Interface mofInterface = (MOF.Interface)packageableElement;
                    EA.Element classElement = childPackage.AddElement(mofInterface.Name, "Interface");

                    AddPrimitiveProperties(mofInterface.OwnedAttributes, classElement);

                    if (!_generatedElements.ContainsKey(mofInterface.FullName))
                    {
                        _generatedElements.Add(mofInterface.FullName, classElement);
                    }
                }
                else if (packageableElement is MOF.Enumeration)
                {
                    MOF.Enumeration mofEnumeration = (MOF.Enumeration)packageableElement;
                    EA.Element enumerationElement = childPackage.AddElement(mofEnumeration.Name, "Enumeration");

                    foreach (MOF.EnumerationLiteral literal in mofEnumeration.OwnedLiterals)
                    {
                        EA.Attribute litearlAttribute = enumerationElement.AddAttribute(literal.Name, "");
                        litearlAttribute.Stereotype = "enum";
                        litearlAttribute.Update();
                    }

                    if (!_generatedElements.ContainsKey(mofEnumeration.FullName))
                    {
                        _generatedElements.Add(mofEnumeration.FullName, enumerationElement);
                    }
                }
            }

            foreach (MOF.Package subPackage in currentPackage.NestedPackages)
            {
                GeneratePackagesAndElementsRecursively(subPackage, childPackage);
            }
        }

        private void AddPrimitiveProperties(List<MOF.Property> properties, EA.Element element)
        {
            // add primitive properties
            foreach (MOF.Property property in properties)
            {
                if (IsPrimitive(property.TypeRef))
                {
                    string? primitiveTypeAlias = GetPrimitiveTypeAlias(property.TypeRef);

                    if (primitiveTypeAlias == null)
                    {
                        primitiveTypeAlias = "";
                    }

                    EA.Attribute attribute = element.AddAttribute(property.Name, primitiveTypeAlias);

                    attribute.Stereotype = "property";
                    attribute.Update();

                    if (property.Annotations != null && property.Annotations.Count > 0)
                    {
                        foreach (MOF.InstanceSpecification annotation in property.Annotations)
                        {
                            AnnotationDescriptor annotationDescriptor = new AnnotationDescriptor
                            {
                                Property = property,
                                Element = element,
                                Attribute = attribute
                            };
                            _unconnectedPrimitiveAnnotations.Add(annotationDescriptor);
                        }
                    }
                }

            }
        }

        private void GenerateConnectorsRecursively(MOF.Package currentPackage)
        {
            foreach (MOF.Base.PackageableElement packageableElement in currentPackage.PackagedElements)
            {
                if (packageableElement is MOF.Class)
                {
                    MOF.Class mofClass = (MOF.Class)packageableElement;

                    if (_generatedElements.ContainsKey(mofClass.FullName))
                    {
                        EA.Element currentEaElement = _generatedElements[mofClass.FullName];

                        foreach (MOF.Property property in mofClass.OwnedAttributes)
                        {
                            GenerateCompositionConnectors(currentEaElement, property);
                        }

                        if (mofClass.SuperClassRefs != null)
                        {
                            foreach (string superClassRef in mofClass.SuperClassRefs)
                            {
                                if (_generatedElements.ContainsKey(superClassRef))
                                {
                                    EA.Element superClassElement = _generatedElements[superClassRef];

                                    if (superClassElement.Type == "Class")
                                    {
                                        EA.Connector generalizationConnector = currentEaElement.AddConnector(superClassElement, "Generalization");
                                    }
                                    else if (superClassElement.Type == "Interface")
                                    {
                                        EA.Connector realizationConnector = currentEaElement.AddConnector(superClassElement, "Realization");
                                    }
                                }
                            }
                        }
                    }
                }
                else if (packageableElement is MOF.Interface)
                {
                    MOF.Interface mofInterface = (MOF.Interface)packageableElement;

                    if (_generatedElements.ContainsKey(mofInterface.FullName))
                    {
                        EA.Element currentEaElement = _generatedElements[mofInterface.FullName];

                        foreach (MOF.Property property in mofInterface.OwnedAttributes)
                        {
                            GenerateCompositionConnectors(currentEaElement, property);
                        }

                        if (mofInterface.RedefinedInterfacesRef != null)
                        {
                            foreach (string superClassRef in mofInterface.RedefinedInterfacesRef)
                            {
                                if (_generatedElements.ContainsKey(superClassRef))
                                {
                                    EA.Element superClassElement = _generatedElements[superClassRef];

                                    EA.Connector generalizationConnector = currentEaElement.AddConnector(superClassElement, "Generalization");
                                }
                            }
                        }
                    }
                }
            }

            foreach (MOF.Package subPackage in currentPackage.NestedPackages)
            {
                GenerateConnectorsRecursively(subPackage);
            }
        }

        private void GenerateCompositionConnectors(EA.Element currentEaElement, MOF.Property property)
        {
            bool isEnumeration = IsEnumeration(property.TypeRef);

            if (!IsPrimitive(property.TypeRef) && !isEnumeration)
            {
                if (_generatedElements.ContainsKey(property.TypeRef))
                {
                    EA.Element oppositeEaElement = _generatedElements[property.TypeRef];

                    EA.Connector aggregationConnector = oppositeEaElement.AddConnector(currentEaElement, "Aggregation");
                    aggregationConnector.ClientEnd.Navigable = "Unspecified";
                    aggregationConnector.ClientEnd.Cardinality = property.Multiplicity;
                    aggregationConnector.ClientEnd.Role = property.Name;
                    aggregationConnector.ClientEnd.Update();

                    aggregationConnector.SupplierEnd.Aggregation = 2;
                    aggregationConnector.SupplierEnd.Cardinality = "1";
                    aggregationConnector.SupplierEnd.Update();

                    if (property.CollectionTypeRef != null)
                    {
                        aggregationConnector.SetTaggedValueString("CollectionTypeRef", property.CollectionTypeRef);
                    }

                    aggregationConnector.Update();

                    GenerateAnnotaionsForComplexType(property, aggregationConnector);
                }
            }
            else if (isEnumeration)
            {

                if (_generatedElements.ContainsKey(property.TypeRef))
                {
                    EA.Element attributeType = _generatedElements[property.TypeRef];

                    EA.Attribute attribute = currentEaElement.AddAttribute(property.Name, attributeType.Name);

                    attribute.ClassifierID = attributeType.ElementID;

                    attribute.Stereotype = "property";
                    attribute.Update();
                }
            }
        }

        private void GenerateAnnotationsForPrimitiveTypes()
        {
            foreach (AnnotationDescriptor annotationDescriptor in _unconnectedPrimitiveAnnotations)
            {
                foreach (MOF.InstanceSpecification annotation in annotationDescriptor.Property.Annotations!)
                {
                    EA.Element? annotationObject = CreateAnnotationObject(annotation, annotationDescriptor.Attribute.Name, annotationDescriptor.Element);

                    annotationDescriptor.Attribute.AddConnector(_repository, annotationObject, "Association");
                }
            }
        }

        private void GenerateAnnotaionsForComplexType(MOF.Property property, EA.Connector aggregationConnector)
        {
            if (property.Annotations != null && property.Annotations.Count > 0)
            {
                EA.Element sourceEaElement = _repository.GetElementByID(aggregationConnector.ClientID);
                EA.Element targetEaElement = _repository.GetElementByID(aggregationConnector.SupplierID);

                EA.Package eaPackage = _repository.GetPackageByID(sourceEaElement.PackageID);

                foreach (MOF.InstanceSpecification annotation in property.Annotations)
                {
                    EA.Element? annotationObject = CreateAnnotationObject(annotation, aggregationConnector.ClientEnd.Role, targetEaElement);

                    if (annotationObject != null)
                    {
                        // generate connector
                        // 1. generate a proxy connector element
                        EA.Element proxyConnectorElement = eaPackage.AddElement("ProxyConnector", "ProxyConnector");

                        _repository.Execute("UPDATE t_object SET Classifier_guid='" + aggregationConnector.ConnectorGUID + "' WHERE Object_ID=" + proxyConnectorElement.ElementID + ";");

                        // 2. add the connector
                        EA.Connector annotationConnector = annotationObject.AddConnector(proxyConnectorElement, "Association");

                    }
                }
            }
        }

        private EA.Element? CreateAnnotationObject(MOF.InstanceSpecification annotation,
                                                   string annotationName,
                                                   EA.Element eaElement)
        {
            EA.Element? result = null;

            if (annotation.ClassifierRef != null && _generatedElements.ContainsKey(annotation.ClassifierRef))
            {
                EA.Element annotationClassifierElement = _generatedElements[annotation.ClassifierRef];

                // generate annotation object
                EA.Element annotationObject = eaElement.AddEmbeddedElement(_repository, annotationName, "Object");
                annotationObject.Stereotype = "annotation";
                annotationObject.ClassifierID = annotationClassifierElement.ElementID;
                annotationObject.Update();

                if (annotation.Slots != null)
                {
                    foreach (MOF.Slot slot in annotation.Slots)
                    {
                        annotationObject.SetRunStateValue(slot.DefiningFeatureRef, slot.Value, "=");
                    }
                }

                result = annotationObject;
            }

            return result;
        }

        private HashSet<string> _primitiveTypes = new HashSet<string>
        {
            "System.Int32",
            "System.String",
            "System.Char",
            "System.Boolean"
        };

        private bool IsPrimitive(string fullName)
        {
            bool result = false;

            if (_primitiveTypes.Contains(fullName))
            {
                result = true;
            }

            return result;
        }

        private bool IsEnumeration(string fullName)
        {
            bool result = false;
            MOF.Base.PackageableElement? packageableElement = FindByFullName(fullName);

            if (packageableElement != null && packageableElement is MOF.Enumeration)
            {
                result = true;
            }

            return result;

        }

        private MOF.Base.PackageableElement? FindByFullName(string fullName)
        {
            MOF.Base.PackageableElement? result = null;
            if (_emofRepository != null)
            {
                foreach (MOF.Package rootPackage in _emofRepository.RootPackages)
                {
                    FindPackagableElementRecursively(rootPackage, fullName, ref result);
                    if (result != null)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        private void FindPackagableElementRecursively(MOF.Package currentPackage, string fullName, ref MOF.Base.PackageableElement? result)
        {
            foreach (MOF.Base.PackageableElement packageableElement in currentPackage.PackagedElements)
            {
                if (packageableElement.FullName == fullName)
                {
                    result = packageableElement;
                    break;
                }
            }
            if (result == null)
            {
                foreach (MOF.Package childPackage in currentPackage.NestedPackages)
                {
                    FindPackagableElementRecursively(childPackage, fullName, ref result);
                }
            }
        }

        private string? GetPrimitiveTypeAlias(string fullName)
        {
            string? result = null;

            switch (fullName)
            {
                case "System.Boolean":
                    result = "bool";
                    break;

                case "System.Int32":
                    result = "int";
                    break;

                case "System.String":
                    result = "string";
                    break;

                case "System.Byte":
                    result = "byte";
                    break;

                case "System.Double":
                    result = "double";
                    break;

                case "System.Float":
                    result = "float";
                    break;
            }

            return result;
        }



        private EA.Package GetOrCreateNamespacePackage(string fullName)
        {
            string[] namespaceTokens = GetNamespacePartsFromFullName(fullName);

            EA.Package result = _targetPackage;
            foreach (string token in namespaceTokens)
            {
                EA.Package childPackage = result.GetChildPackageByName(token);

                if (childPackage == null)
                {
                    childPackage = result.AddChildPackage(token);
                }
                result = childPackage;
            }


            return result;
        }

        private string GetClassNameFromFullName(string fullName)
        {
            string[] tokens = fullName.Split('.');

            return tokens[tokens.Length - 1];
        }

        private string[] GetNamespacePartsFromFullName(string fullName)
        {
            string[] tokens = fullName.Split('.');

            return tokens.Take(tokens.Count() - 1).ToArray();
        }
    }
}
