// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
import React, {
    useContext,
    useEffect,
    useReducer,
    useState,
    Fragment } from 'react';
import { Modal, ModalHeader, ModalBody, ModalFooter } from 'reactstrap';
import { useNavigate } from 'react-router-dom';
import ApiService from '../services/ApiService';
import { TeamsContext } from '../App';

import * as teamsjs from "@microsoft/teams-js";
const { authentication } = teamsjs;

const Connections = (props) => {
    const navigate = useNavigate();
    const { tabContext } = useContext(TeamsContext);

    const initState = {
        connections: [],
        showConnectionModal: false,
        formState: {
            displayName: "",
            baseUrl: "",
            tokenUrl: "",
            clientId: "",
            clientSecret: "",
            editConnectionId: "",
            isGroupEnabled: false,
            allowNoneLineItemCategory: false,
            defaultLineItemCategory: null,
            categories: null
        },
        formComplete: false,
        formError: false,
        formErrorMessage: "",
        isSaving: false,
        actionToast: {
            showToast: false,
            text: ""
        },
        deleteModal: {
            show: false,
            connectionId: ""
        }
    }

    const stateReducer = (state, action) => {
        const stateCopy = {...state};

        if (action.type === "setConnections") {
            stateCopy.connections = action.connections;
        }

        if (action.type === "hideModal") {
            stateCopy.formState = {
                displayName: "",
                baseUrl: "",
                tokenUrl: "",
                clientId: "",
                clientSecret: "",
                editConnectionId: "",
                isGroupEnabled: false,
                allowNoneLineItemCategory: false,
                defaultLineItemCategory: null,
                categories: null
            };
            stateCopy.formError = false;
            stateCopy.formErrorMessage = "";
            stateCopy.isSaving = false;
            stateCopy.formComplete = false;
            stateCopy.showConnectionModal = false;
        }

        if (action.type === "showModal") {
            stateCopy.showConnectionModal = true;

            if (action.dto) {
                stateCopy.formState.displayName = action.dto.displayName;
                stateCopy.formState.editConnectionId = action.dto.connectionId;
                stateCopy.formState.isGroupEnabled = action.dto.isGroupEnabled;
                stateCopy.formState.allowNoneLineItemCategory = action.dto.allowNoneLineItemCategory;
                stateCopy.formState.defaultLineItemCategory = action.dto.defaultLineItemCategory;
                stateCopy.formState.categories = action.categories;

                stateCopy.formState.baseUrl = action.detailsDto.oneRosterBaseUrl;
                stateCopy.formState.tokenUrl = action.detailsDto.oAuth2TokenUrl;
                stateCopy.formState.clientId = action.detailsDto.clientId;
            }
        }

        if (action.type === "hideDeleteModal") {
            stateCopy.deleteModal.show = false;
            stateCopy.deleteModal.connectionId = "";
        }

        if (action.type === "showDeleteModal") {
            stateCopy.deleteModal.show = true;
            stateCopy.deleteModal.connectionId = action.id;
        }

        if (action.type === "fieldChange") {
            stateCopy.formState[action.field] = action.val;

            let allFieldsHaveValue = true;
            for (const [key, value] of Object.entries(stateCopy.formState)) {
                if (value === ""
                    && key !== "editConnectionId"
                    && key !== "isGroupEnabled"
                    && key !== "allowNoneLineItemCategory"
                    && key !== "defaultLineItemCategory"
                    && key !== "categories"
                ) {
                    allFieldsHaveValue = false;
                    break;
                }
            }

            stateCopy.formComplete = allFieldsHaveValue;
        }

        if (action.type === "changeDefaultCat") {
            stateCopy.formState.defaultLineItemCategory = action.catId;
        }

        if (action.type === "checkboxChange") {
            stateCopy.formState[action.field] = !stateCopy.formState[action.field];
        }

        if (action.type === "formError") {
            stateCopy.formError = action.val;
            stateCopy.formErrorMessage = action.message;
        }

        if (action.type === "saving") {
            stateCopy.isSaving = action.val;
        }

        if (action.type === "modifyToast") {
            stateCopy.actionToast.showToast = action.showToast;
            stateCopy.actionToast.text = action.text;
        }

        if (action.type === "setDefaultConnection") {
            for (const connection of stateCopy.connections) {
                if (connection.connectionId === action.id) {
                    connection.isDefaultConnection = true;
                } else {
                    connection.isDefaultConnection = false;
                }
            }
        }

        if (action.type === "insertConnection") {
            stateCopy.connections.push({
                connectionId: action.id,
                displayName: action.displayName,
                canEdit: true,
                isDefaultConnection: false,
                isGroupEnabled: action.groupEnabled
            });
        }

        if (action.type === "modifyConnection") {
            for (const connection of stateCopy.connections) {
                if (connection.connectionId === action.id) {
                    connection.displayName = action.displayName;
                    connection.isGroupEnabled = action.groupEnabled;
                    break;
                }
            }
        }

        if (action.type === "spliceConnection") {
            const newConnections = [];
            for (const connection of stateCopy.connections) {
                if (connection.connectionId !== action.id) newConnections.push(connection);
            }

            stateCopy.connections = newConnections;
        }

        if (action.type === "trimFormWhitespace") {
            stateCopy.formState.displayName = stateCopy.formState.displayName.trim();
            stateCopy.formState.baseUrl = stateCopy.formState.baseUrl.trim();
            stateCopy.formState.tokenUrl = stateCopy.formState.tokenUrl.trim();
            stateCopy.formState.clientId = stateCopy.formState.clientId.trim();
            stateCopy.formState.clientSecret = stateCopy.formState.clientSecret.trim();
        }

        return stateCopy;
    }
    const [dataState, dispatch] = useReducer(stateReducer, initState);
    
    const getConnections = async () => {
        await teamsjs.app.initialize();
        const token = await authentication.getAuthToken();
        const res = await ApiService.apiGetRequest(token, `/api/get-one-roster-connections/${props.isAdmin}`);
        dispatch({ type: "setConnections", connections: res.data});
    }

    const onSubmitConnection = async () => {
        dispatch({ type: "saving", val: true});
        if (dataState.formError) dispatch({ type: "formError", val: false });

        const token = await authentication.getAuthToken();

        try {
            dispatch({ type: "trimFormWhitespace" });
            const { categories, ...payload } = dataState.formState;
            const res = await ApiService.apiPostRequest(token, "/api/create-one-roster-connection", payload);
            const toastText = `Successfully ${dataState.formState.editConnectionId ? "edited" : "created"} API connection: ${dataState.formState.displayName}`;
            dispatch({ type: "modifyToast", showToast: true, text: toastText });

            if (dataState.formState.editConnectionId) {
                dispatch({ 
                    type: "modifyConnection",
                    id: dataState.formState.editConnectionId,
                    displayName: dataState.formState.displayName,
                    groupEnabled: dataState.formState.isGroupEnabled
                });
            } else {
                dispatch({ 
                    type: "insertConnection",
                    id: res.data,
                    displayName: dataState.formState.displayName,
                    groupEnabled: dataState.formState.isGroupEnabled
                });
            }
            onCloseModal();
        } catch (e) {
            dispatch({ type: "saving", val: false});
            dispatch({ type: "formError", val: true, message: e.response.data });
        }
    }

    const setDefaultConnection = async (connectionDto) => {
        dispatch({type: "setDefaultConnection", id: connectionDto.connectionId});

        const token = await authentication.getAuthToken();
        await ApiService.apiPostRequest(token, `/api/set-default-connection/${connectionDto.connectionId}`, null);
    }

    const onDeleteConnection = async () => {
        const token = await authentication.getAuthToken();
        await ApiService.apiPostRequest(token, `/api/delete-connection/${dataState.deleteModal.connectionId}`, null);

        dispatch({ type: "spliceConnection", id: dataState.deleteModal.connectionId });
        onCloseDeleteModal();
        dispatch({ type: "modifyToast", showToast: true, text: "Successfully deleted API connection." }); 
    }

    const onEditOpen = async (connectionDto) => {
        const token = await authentication.getAuthToken();
        const res = await ApiService.apiGetRequest(token, `/api/get-all-categories/${connectionDto.connectionId}`);
        const connectionRes = await ApiService.apiGetRequest(token, `/api/get-connection-details/${connectionDto.connectionId}`);
        dispatch({ type: "showModal", dto: connectionDto, categories: res.data, detailsDto: connectionRes.data });
    }

    useEffect(() => {
        if (tabContext) {
            getConnections();
        }
    }, [tabContext]);

    const onBack = (evt) => {
        evt.preventDefault();
        navigate("/");
    }

    const onCloseModal = () => {
        dispatch({ type: "hideModal" });
    }

    const onCloseDeleteModal = () => {
        dispatch({ type: "hideDeleteModal"});
    }

    const onShowDeleteModal = (connectionId) => {
        dispatch({ type: "showDeleteModal", id: connectionId });
    }

    const onFieldChange = (evt, fieldName) => {
        dispatch({ type: "fieldChange", field: fieldName, val: evt.target.value })
    }

    const onCheckboxChange = (fieldName) => {
        dispatch({ type: "checkboxChange", field: fieldName})
    }

    const getTextInput = (stateReference, fieldName, placeholder, title, inputType) => {
        return (
            <div className="mb-3">
                <label className="form-label required" for={fieldName}>{title}</label>
                <input 
                    className="form-control"
                    id={fieldName}
                    type={inputType} 
                    placeholder={placeholder}
                    value={stateReference}
                    onChange={e => onFieldChange(e, fieldName)}
                />     
            </div>
        );
    }

    const getCheckboxInput = (stateReference, fieldName, label) => {
        return (
            <div className="form-check mb-3">
                <label className="form-check-label" for={fieldName}>{label}</label>
                <input 
                    className="form-check-input"
                    id={fieldName}
                    type="checkbox" 
                    checked={stateReference}
                    onChange={e => onCheckboxChange(fieldName)}
                />     
            </div>
        );
    }

    const getCategoriesSelect = (fieldName, label) => {
        return (
            <div className="form-check mb-3">
                <label className="form-check-label" for={fieldName}>{label}</label>
                <select
                    className="form-select"
                    id={fieldName}
                    value={dataState.formState.defaultLineItemCategory}
                    onChange={e => dispatch({ type: "changeDefaultCat", catId: e.target.value })}
                >
                    {
                        dataState.formState.categories.map((category) => {
                            return (
                                <option value={category.id}>{category.title} (ID: {category.id})</option>
                            );
                        })
                    }
                </select>
            </div>
        );
    }

    const getFormErrorMessage = () => {
        if (dataState.formError) {
            const message = dataState.formErrorMessage && dataState.formErrorMessage !== "" 
                ? dataState.formErrorMessage
                : "Error validating OneRoster API connection. Verify inputs are correct.";

            return (
                <div className="alert alert-danger mb-3" role="alert">
                    <strong>Error: </strong> {message}
                </div>
            );
        } else return null;
    }

    const savingSpinner = () => {
        if (dataState.isSaving) {
            return (
                <span className="my-auto me-3">
                    <div className="spinner-border text-primary my-auto">
                        <span className="visually-hidden">Syncing</span>
                    </div>
                </span>
            );
        } else return null;
    }

    const getFormModal = () => {
        const titleText = dataState.formState.editConnectionId 
            ? "Edit API Connection"
            : "Add New API Connection";
        return (
            <Modal isOpen={dataState.showConnectionModal} toggle={onCloseModal}>
                <ModalHeader toggle={onCloseModal}>{titleText}</ModalHeader>
                <ModalBody>
                    { getTextInput(dataState.formState.displayName, "displayName", "Connection name", "Name", "text") }
                    { getTextInput(dataState.formState.baseUrl, "baseUrl", "Enter OneRoster base URL", "OneRoster Base URL", "text") }
                    { getTextInput(dataState.formState.tokenUrl, "tokenUrl", "Enter OneRoster token URL", "OneRoster Token URL", "text") }
                    { getTextInput(dataState.formState.clientId, "clientId", "Enter OneRoster Client ID", "OneRoster Client ID", "text") }
                    { getTextInput(dataState.formState.clientSecret, "clientSecret", "Enter OneRoster Client Secret", "OneRoster Client Secret", "password") }
                    { getCheckboxInput(dataState.formState.isGroupEnabled, "isGroupEnabled", "Group-enabled") }
                    { getCheckboxInput(dataState.formState.allowNoneLineItemCategory, "allowNoneLineItemCategory", "Allow 'None' assignment category") }

                    {
                        dataState.formState.allowNoneLineItemCategory && dataState.formState.categories && dataState.formState.editConnectionId ?
                            getCategoriesSelect("categories", "Default line-item category")
                            : null
                    }

                    { getFormErrorMessage() }
                </ModalBody>
                <ModalFooter>
                    { savingSpinner() }
                    <button type="button" className="btn btn-secondary" onClick={onCloseModal}>Cancel</button>
                    {
                        dataState.formComplete 
                            ? <button type="button" className="btn btn-primary" onClick={onSubmitConnection}>Save</button> 
                            : <button type="button" className="btn btn-primary" disabled>Save</button> 
                    }
                </ModalFooter>
            </Modal>
        );
    }

    const getDeleteModal = () => {
        return (
            <Modal isOpen={dataState.deleteModal.show} toggle={onCloseDeleteModal}>
                <ModalHeader toggle={onCloseDeleteModal}>Delete API Connection</ModalHeader>
                <ModalBody>
                    Are you sure you want to delete this API connection?
                </ModalBody>
                <ModalFooter>
                    <button type="button" className="btn btn-secondary" onClick={onCloseDeleteModal}>Cancel</button>
                    <button type="button" className="btn btn-danger" onClick={onDeleteConnection}>Delete</button> 
                </ModalFooter>
            </Modal>
        );
    }

    const getConnectionCard = (connectionDto) => {
        return(
            <div className="card mb-3">
                <div className="card-body">
                    <div className="d-flex flex-row">
                        <div className="d-flex">
                            <h5 className="card-title my-auto">{connectionDto.displayName}</h5>
                        </div>
                        <div className="d-flex ms-auto">
                            <span className="float-end my-auto">
                                <div className="form-check form-switch mb-0">
                                    <input 
                                        className="form-check-input" 
                                        type="checkbox"
                                        checked={connectionDto.isDefaultConnection}
                                        disabled={connectionDto.isDefaultConnection}
                                        onChange={() => setDefaultConnection(connectionDto)}
                                    />
                                </div>
                            </span>
                        </div>
                    </div>
                </div>

                <div className="card-footer">
                    <span className="float-start my-auto">
                        <div className="form-check my-auto">
                            <label className="form-check-label" for={`groupEnabled${connectionDto.connectionId}`}>Group-enabled</label>
                            <input 
                                className="form-check-input"
                                id={`groupEnabled${connectionDto.connectionId}`}
                                type="radio" 
                                checked={connectionDto.isGroupEnabled}
                                disabled
                            /> 
                        </div>
                    </span>

                    {
                        connectionDto.canEdit ?
                            <span className="float-end my-auto">
                                <button className="btn btn-danger" onClick={() => onShowDeleteModal(connectionDto.connectionId)}>Delete</button>
                            </span>
                            : null
                    }
                    {
                        connectionDto.canEdit ?
                            <span className="float-end my-auto me-2">
                                <button className="btn btn-outline-primary" onClick={() => onEditOpen(connectionDto)}>Edit</button>
                            </span>
                            : null
                    }
                </div>  
            </div>
        );
    }

    const getSuccessToast = () => {
        if (dataState.actionToast.showToast) {
            return (
                <div className="position-fixed bottom-0 end-0 p-3" style={{ zIndex: 11 }}>
                    <div className="toast show align-items-center text-white bg-primary" data-bs-autohide="false" role="alert">
                        <div className="d-flex">
                            <div className="toast-body">
                                {dataState.actionToast.text}
                            </div>
                            <button 
                                className="btn-close btn-close-white me-2 m-auto"
                                onClick={() => dispatch({ type: "modifyToast", showToast: false, text: ""})}
                            >
                            </button>
                        </div>
                    </div>
                </div>
            );
        } else return null;
    }

    return (
        <Fragment>
            <nav className="ms-3">
                <ol className="breadcrumb">
                    <li className="breadcrumb-item"><a href="#" onClick={e => onBack(e)}>Assignments</a></li>
                    <li className="breadcrumb-item active">Manage API Connections</li>
                </ol>
            </nav>
            <hr/>
            
            <div className="row justify-content-center mt-4">
                <div className="col-8">

                    <div className="d-flex flex-row mb-5">
                        <div className="d-flex">
                            <h3>Manage API Connections</h3>
                        </div>

                        <div className="d-flex ms-auto">
                            <span className="float-end my-auto">
                                {
                                    props.isAdmin 
                                    ? <button type="button" className="btn btn-primary" onClick={() => dispatch({type: "showModal"})}>+ Add New Connection</button>
                                    : null
                                } 
                            </span>
                        </div>
                    </div>

                    <div className="container-fluid p-0">
                        <div className="row">
                            <div className="col-lg-6 col-12">
                                {
                                    dataState.connections.map((connection) => { return getConnectionCard(connection) })
                                }
                            </div>
                        </div>
                    </div>

                    { getFormModal() }
                    { getDeleteModal() }
                </div>
            </div>

            { getSuccessToast() }
        </Fragment>
    );
}

export default Connections;
