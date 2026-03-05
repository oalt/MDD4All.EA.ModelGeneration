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

        private Dictionary<string, EA.Element> _elementsToConnect = new Dictionary<string, EA.Element>();

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
            _elementsToConnect = new Dictionary<string, EA.Element>();
            _unconnectedPrimitiveAnnotations = new List<AnnotationDescriptor>();

            string emofJson = File.ReadAllText(_pathToSchema);

            _emofRepository = JsonConvert.DeserializeObject<MOF.EmofRepository>(emofJson,
                                                                                new JsonSerializerSettings
                                                                                {
                                                                                    TypeNameHandling = TypeNameHandling.Auto
                                                                                });

            if (_emofRepository != null)
            {
                if (!_targetPackage.IsNamespace)
                {
                    _targetPackage.IsNamespace = true;
                    _targetPackage.Update();
                }


                //EA.Diagram metamodelDiagram = _targetPackage.AddDiagram("Class");

                foreach (MOF.Package package in _emofRepository.RootPackages)
                {
                    GeneratePackagesAndElementsRecursively(package);
                }

                foreach (MOF.Package package in _emofRepository.RootPackages)
                {
                    GenerateConnectorsRecursively(package);
                }

                //GenerateAnnotationsForPrimitiveTypes();

                //_repository.GetProjectInterface().LayoutDiagram(metamodelDiagram.DiagramGUID, 0);

                _targetPackage.Element.Update();
            }
        }



        private void GeneratePackagesAndElementsRecursively(MOF.Package currentPackage)
        {
            foreach (MOF.Base.PackageableElement packageableElement in currentPackage.PackagedElements)
            {
                EA.Package elementPackage = GetOrCreateNamespacePackage(packageableElement.FullName);

                string mofVersion = "1.0.0.0";

                if (packageableElement.Version != null)
                {
                    mofVersion = packageableElement.Version;
                }

                if(packageableElement.Name == "Note")
                {
                    ;
                }

                EA.Element? existingElement = GetElementByFullNameAndVersion(packageableElement.FullName, mofVersion);

                bool createNewElement = false;
                bool updateElement = false;

                if (existingElement == null)
                {
                    createNewElement = true;
                    updateElement = true;
                }

                if (existingElement != null && existingElement.Version == "1.0.0.0")
                {
                    // update existing element
                    createNewElement = false;
                    updateElement = true;

                    // delete all existing connectors
                    for (int index = existingElement.Connectors.Count - 1; index >= 0; index--)
                    {
                        existingElement.Connectors.Delete((short)index);
                        existingElement.Connectors.Refresh();
                    }

                    if (!_elementsToConnect.ContainsKey(packageableElement.FullName))
                    {
                        _elementsToConnect.Add(packageableElement.FullName, existingElement);
                    }
                }

                if (updateElement)
                {
                    if (packageableElement is MOF.Class)
                    {
                        MOF.Class mofClass = (MOF.Class)packageableElement;

                        EA.Element? classElement = null;

                        if (createNewElement)
                        {
                            classElement = elementPackage.AddElement(mofClass.Name, "Class");
                            _elementsToConnect.Add(mofClass.FullName, classElement);
                        }
                        else
                        {
                            classElement = existingElement;
                        }

                        classElement!.Version = mofClass.Version;

                        if (mofClass.IsAbstract)
                        {
                            classElement.Abstract = "1";
                        }
                        classElement.Update();

                        AddOrUpdatePrimitiveProperties(mofClass.OwnedAttributes, classElement);

                        if (!_generatedElements.ContainsKey(mofClass.FullName))
                        {
                            _generatedElements.Add(mofClass.FullName, classElement);
                        }

                    }
                    else if (packageableElement is MOF.Interface)
                    {
                        MOF.Interface mofInterface = (MOF.Interface)packageableElement;
                        EA.Element? classElement = null;

                        if (createNewElement)
                        {
                            classElement = elementPackage.AddElement(mofInterface.Name, "Interface");
                            _elementsToConnect.Add(mofInterface.FullName, classElement);
                        }
                        else
                        {
                            classElement = existingElement!;
                        }

                        classElement!.Version = mofInterface.Version;
                        classElement.Update();

                        AddOrUpdatePrimitiveProperties(mofInterface.OwnedAttributes, classElement);

                        if (!_generatedElements.ContainsKey(mofInterface.FullName))
                        {
                            _generatedElements.Add(mofInterface.FullName, classElement);
                        }
                    }
                    else if (packageableElement is MOF.Enumeration)
                    {
                        MOF.Enumeration mofEnumeration = (MOF.Enumeration)packageableElement;
                        EA.Element? enumerationElement = null;

                        if (createNewElement)
                        {
                            enumerationElement = elementPackage.AddElement(mofEnumeration.Name, "Enumeration");
                            _elementsToConnect.Add(mofEnumeration.FullName, enumerationElement);
                        }
                        else
                        {
                            enumerationElement = existingElement!;
                        }

                        enumerationElement.Version = mofEnumeration.Version;
                        enumerationElement.Update();
                        AddOrUpdateEnumerationValues(mofEnumeration, enumerationElement);

                        if (!_generatedElements.ContainsKey(mofEnumeration.FullName))
                        {
                            _generatedElements.Add(mofEnumeration.FullName, enumerationElement);
                        }
                    }
                }
            }

            foreach (MOF.Package subPackage in currentPackage.NestedPackages)
            {
                GeneratePackagesAndElementsRecursively(subPackage);
            }
        }

        private void AddOrUpdateEnumerationValues(MOF.Enumeration mofEnumeration, EA.Element enumerationElement)
        {
            Dictionary<string, EA.Attribute> existingAttributes = GetExistingAttributes(enumerationElement);

            foreach (MOF.EnumerationLiteral literal in mofEnumeration.OwnedLiterals)
            {
                if (existingAttributes.ContainsKey(literal.Name))
                {
                    // value still exists
                    // remove from dictionary
                    existingAttributes.Remove(literal.Name);
                }
                else
                {
                    EA.Attribute litearlAttribute = enumerationElement.AddAttribute(literal.Name, "");
                    litearlAttribute.Stereotype = "enum";
                    litearlAttribute.Update();
                }
            }

            // delete unused attributes
            foreach (KeyValuePair<string, EA.Attribute> entry in existingAttributes)
            {
                enumerationElement.DeleteAttribute(entry.Value);
            }
        }

        private void AddOrUpdatePrimitiveProperties(List<MOF.Property> properties, EA.Element element)
        {
            Dictionary<string, EA.Attribute> existingAttributes = GetExistingAttributes(element);

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

                    EA.Attribute? attribute = null;

                    if (existingAttributes.ContainsKey(property.Name))
                    {
                        attribute = existingAttributes[property.Name];
                        existingAttributes.Remove(property.Name);
                    }
                    else
                    {
                        attribute = element.AddAttribute(property.Name, primitiveTypeAlias);
                    }

                    if (property.Kind == MOF.Enumerations.PropertyKind.Property)
                    {
                        attribute.Stereotype = "property";
                    }

                    if (property.IsReadOnly)
                    {
                        attribute.IsConst = true;
                    }
                    else
                    {
                        attribute.IsConst = false;
                    }

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

            // delete unused attributes
            foreach (KeyValuePair<string, EA.Attribute> entry in existingAttributes)
            {
                element.DeleteAttribute(entry.Value);
            }
        }

        private Dictionary<string, EA.Attribute> GetExistingAttributes(EA.Element element)
        {
            Dictionary<string, EA.Attribute> result = new Dictionary<string, EA.Attribute>();

            for (short counter = 0; counter < element.Attributes.Count; counter++)
            {
                EA.Attribute attribute = (EA.Attribute)element.Attributes.GetAt(counter);
                result.Add(attribute.Name, attribute);
            }
            return result;
        }

        private void GenerateConnectorsRecursively(MOF.Package currentPackage)
        {
            foreach (MOF.Base.PackageableElement packageableElement in currentPackage.PackagedElements)
            {
                if (packageableElement is MOF.Class)
                {
                    MOF.Class mofClass = (MOF.Class)packageableElement;

                    if (_elementsToConnect.ContainsKey(mofClass.FullName))
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

                    if (_elementsToConnect.ContainsKey(mofInterface.FullName))
                    {
                        EA.Element currentEaElement = _generatedElements[mofInterface.FullName];

                        foreach (MOF.Property property in mofInterface.OwnedAttributes)
                        {
                            GenerateCompositionConnectors(currentEaElement, property);
                        }

                        if (mofInterface.RedefinedInterfacesRefs != null)
                        {
                            foreach (string superClassRef in mofInterface.RedefinedInterfacesRefs)
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
                else if (packageableElement is MOF.Association)
                {
                    MOF.Association association = (MOF.Association)packageableElement;

                    if (association.OwnedEnds.Count == 2)
                    {
                        MOF.Property sourceProperty = association.OwnedEnds[0];
                        MOF.Property targetProperty = association.OwnedEnds[1];

                        if (sourceProperty != null && targetProperty != null)
                        {
                            EA.Element? sourceEaElement = null;
                            EA.Element? targetEaElement = null;

                            if (_elementsToConnect.ContainsKey(sourceProperty.TypeRef))
                            {
                                if (_generatedElements.ContainsKey(sourceProperty.TypeRef))
                                {
                                    sourceEaElement = _generatedElements[sourceProperty.TypeRef];
                                }

                                if (_generatedElements.ContainsKey(targetProperty.TypeRef))
                                {
                                    targetEaElement = _generatedElements[targetProperty.TypeRef];
                                }

                                if (sourceEaElement != null && targetEaElement != null)
                                {
                                    EA.Connector associationConnector = sourceEaElement.AddConnector(targetEaElement, "Association");

                                    associationConnector.ClientEnd.Cardinality = sourceProperty.Multiplicity;
                                    associationConnector.ClientEnd.Navigable = "Navigable";
                                    associationConnector.ClientEnd.Update();

                                    associationConnector.SupplierEnd.Cardinality = targetProperty.Multiplicity;
                                    associationConnector.SupplierEnd.Role = targetProperty.Name;
                                    associationConnector.SupplierEnd.Update();

                                    associationConnector.Update();


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
                        aggregationConnector.SetTaggedValueString("MemberKind", property.Kind.ToString());
                    }

                    aggregationConnector.Update();

                    //GenerateAnnotaionsForComplexType(property, aggregationConnector);
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
                        aggregationConnector.AddConnector(_repository, annotationObject, "Association");
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

        private EA.Element? GetElementByFullNameAndVersion(string fullName, string version)
        {
            EA.Element? result = null;

            string name = GetClassNameFromFullName(fullName);

            EA.Package package = GetOrCreateNamespacePackage(fullName);

            for (short counter = 0; counter < package.Elements.Count; counter++)
            {
                EA.Element element = (EA.Element)package.Elements.GetAt(counter);

                if (element.Name == name && element.Version == version)
                {
                    result = element;
                    break;
                }
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
